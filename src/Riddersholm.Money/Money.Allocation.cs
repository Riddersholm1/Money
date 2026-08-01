namespace Riddersholm.Money;

/// <content>
/// Allocation: splitting an amount so that the parts add back up to exactly the whole.
/// </content>
/// <remarks>
/// Division cannot do this. <c>10 DKK / 3</c> is <c>3.333…</c>, and three of those rounded to øre come
/// to <c>9.99 DKK</c> — a øre has evaporated. Allocation distributes the indivisible remainder instead,
/// giving <c>3.34</c>, <c>3.33</c>, <c>3.33</c>. The sum is exact, always, by construction.
/// </remarks>
public readonly partial record struct Money
{
    /// <summary>Splits the amount into <paramref name="count"/> as-equal-as-possible parts.</summary>
    /// <param name="count">How many parts to split into.</param>
    /// <returns>
    /// The parts, in order. Where the amount does not divide evenly, the earlier parts each receive one
    /// extra minor unit, so the result is deterministic.
    /// </returns>
    /// <remarks>
    /// <c>10 DKK</c> across three recipients gives <c>3.34</c>, <c>3.33</c>, <c>3.33</c>. Negative
    /// amounts behave symmetrically: <c>-10 DKK</c> gives <c>-3.34</c>, <c>-3.33</c>, <c>-3.33</c>.
    /// </remarks>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="count"/> is not positive.</exception>
    /// <exception cref="UnknownCurrencyException">The currency has no known minor unit to allocate in.</exception>
    /// <exception cref="InvalidOperationException">
    /// The amount is not <see cref="IsCanonical">canonical</see>, so no set of payable parts could sum
    /// to it. Round first.
    /// </exception>
    public Money[] Allocate(int count)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(count);

        Money[] result = new Money[count];
        Allocate(result.AsSpan());
        return result;
    }

    /// <summary>Splits the amount into as-equal-as-possible parts, writing into a caller-owned buffer.</summary>
    /// <param name="destination">Receives one part per element; its length determines the split.</param>
    /// <remarks>Allocates nothing, so a stack buffer works for small splits.</remarks>
    /// <exception cref="ArgumentException"><paramref name="destination"/> is empty.</exception>
    /// <exception cref="UnknownCurrencyException">The currency has no known minor unit to allocate in.</exception>
    /// <exception cref="InvalidOperationException">The amount is not canonical.</exception>
    public void Allocate(Span<Money> destination)
    {
        if (destination.IsEmpty)
        {
            throw new ArgumentException("Cannot allocate across zero recipients.", nameof(destination));
        }

        decimal units = ToMinorUnits(out long unitsPerMajor);
        int count = destination.Length;

        // Truncation is toward zero, so a negative total distributes its remainder the same way a
        // positive one does and the parts stay symmetric under negation.
        if (TryUseIntegerPath(units, unitsPerMajor, out long integerUnits, out byte scale))
        {
            long each = integerUnits / count;
            long remainder = integerUnits - (each * count);
            int extra = (int)Math.Abs(remainder);
            long step = Math.Sign(remainder);

            for (int i = 0; i < count; i++)
            {
                destination[i] = new Money(FromMinorUnits(i < extra ? each + step : each, scale), Currency);
            }

            return;
        }

        decimal decimalEach = decimal.Truncate(units / count);
        decimal decimalRemainder = units - (decimalEach * count);
        int decimalExtra = (int)Math.Abs(decimalRemainder);
        decimal decimalStep = Math.Sign(decimalRemainder);

        for (int i = 0; i < count; i++)
        {
            decimal share = i < decimalExtra ? decimalEach + decimalStep : decimalEach;
            destination[i] = new Money(share / unitsPerMajor, Currency);
        }
    }

    /// <summary>Splits the amount in proportion to integer weights.</summary>
    /// <param name="ratios">The weights. They need not sum to anything in particular.</param>
    /// <returns>The parts, in the same order as <paramref name="ratios"/>.</returns>
    /// <remarks>
    /// <c>100 DKK</c> split <c>70:30</c> gives <c>70 DKK</c> and <c>30 DKK</c>. Where the split is not
    /// exact, the remaining minor units go to the parts with the largest fractional shortfall — the
    /// largest-remainder method — with ties resolved by position so the result is deterministic.
    /// </remarks>
    /// <exception cref="ArgumentException">
    /// <paramref name="ratios"/> is empty, contains a negative weight, or sums to zero.
    /// </exception>
    /// <exception cref="UnknownCurrencyException">The currency has no known minor unit to allocate in.</exception>
    /// <exception cref="InvalidOperationException">The amount is not canonical.</exception>
    public Money[] Allocate(ReadOnlySpan<int> ratios)
    {
        Money[] result = new Money[ratios.Length];
        Allocate(ratios, result.AsSpan());
        return result;
    }

    /// <inheritdoc cref="Allocate(ReadOnlySpan{int})" />
    public Money[] Allocate(ReadOnlySpan<decimal> ratios)
    {
        Money[] result = new Money[ratios.Length];
        Allocate(ratios, result.AsSpan());
        return result;
    }

    /// <inheritdoc cref="Allocate(ReadOnlySpan{int})" />
    /// <param name="ratios">The weights.</param>
    /// <param name="destination">Receives one part per weight; must be the same length as <paramref name="ratios"/>.</param>
    public void Allocate(ReadOnlySpan<int> ratios, Span<Money> destination)
    {
        if (ratios.Length <= 16)
        {
            Span<decimal> converted = stackalloc decimal[ratios.Length];
            Widen(ratios, converted);
            Allocate(converted, destination);
            return;
        }

        decimal[] rented = new decimal[ratios.Length];
        Widen(ratios, rented);
        Allocate(rented, destination);

        static void Widen(ReadOnlySpan<int> source, Span<decimal> target)
        {
            for (int i = 0; i < source.Length; i++)
            {
                target[i] = source[i];
            }
        }
    }

    /// <inheritdoc cref="Allocate(ReadOnlySpan{int}, Span{Money})" />
    public void Allocate(ReadOnlySpan<decimal> ratios, Span<Money> destination)
    {
        if (ratios.IsEmpty)
        {
            throw new ArgumentException("At least one ratio is required.", nameof(ratios));
        }

        if (destination.Length != ratios.Length)
        {
            throw new ArgumentException(
                $"Expected {ratios.Length} destination elements to match the ratios, but found {destination.Length}.",
                nameof(destination));
        }

        decimal total = 0m;

        foreach (decimal ratio in ratios)
        {
            if (ratio < 0m)
            {
                throw new ArgumentException("Ratios cannot be negative.", nameof(ratios));
            }

            total += ratio;
        }

        if (total == 0m)
        {
            throw new ArgumentException("Ratios must sum to more than zero.", nameof(ratios));
        }

        decimal units = ToMinorUnits(out long unitsPerMajor);
        int count = ratios.Length;
        bool integerPath = TryUseIntegerPath(units, unitsPerMajor, out _, out byte scale);

        // Shortfalls are needed twice, so they are computed once. A stack buffer covers the sizes
        // money is realistically split across; larger splits fall back to the heap.
        Span<decimal> shortfalls = count <= 32 ? stackalloc decimal[32] : new decimal[count];
        shortfalls = shortfalls[..count];

        Span<decimal> shares = count <= 32 ? stackalloc decimal[32] : new decimal[count];
        shares = shares[..count];

        decimal assigned = 0m;

        for (int i = 0; i < count; i++)
        {
            // Divide before multiplying. The other order overflows whenever the weights are large —
            // and passing raw amounts as weights is an entirely natural thing to do.
            decimal exact = units * (ratios[i] / total);
            decimal share = decimal.Truncate(exact);

            shares[i] = share;
            shortfalls[i] = Math.Abs(exact - share);
            assigned += share;
        }

        // Hand out what truncation left behind, one minor unit at a time, to whoever was shortchanged
        // most — the largest-remainder method. Ties go to the earlier position, so the split is
        // reproducible.
        decimal remainder = units - assigned;
        decimal step = Math.Sign(remainder);
        decimal outstandingExact = Math.Abs(remainder);

        // Each truncation loses strictly less than one unit, so the outstanding count is always below
        // the number of recipients — but the loop below marks each winner as spent, and if that ever
        // stopped being true index 0 would take the surplus repeatedly, breaking the documented
        // guarantee that parts differ by at most one minor unit. Rather than rest on the reasoning,
        // hand everyone the whole part of the surplus up front. What remains is provably less than
        // `count`, which also makes the cast below safe. In practice this branch is never taken.
        decimal bulk = decimal.Truncate(outstandingExact / count);

        if (bulk > 0m)
        {
            decimal bulkStep = bulk * step;

            for (int i = 0; i < count; i++)
            {
                shares[i] += bulkStep;
            }

            outstandingExact -= bulk * count;
        }

        for (int outstanding = (int)outstandingExact; outstanding > 0; outstanding--)
        {
            int best = 0;
            decimal bestShortfall = shortfalls[0];

            for (int i = 1; i < count; i++)
            {
                if (shortfalls[i] > bestShortfall)
                {
                    bestShortfall = shortfalls[i];
                    best = i;
                }
            }

            shares[best] += step;
            shortfalls[best] = -1m;
        }

        for (int i = 0; i < count; i++)
        {
            destination[i] = new Money(
                integerPath ? FromMinorUnits((long)shares[i], scale) : shares[i] / unitsPerMajor,
                Currency);
        }
    }

    /// <summary>
    /// Whether the split can be done in <see cref="long"/> arithmetic with an exact scale, which
    /// avoids a <see cref="decimal"/> division per recipient.
    /// </summary>
    /// <remarks>
    /// Benchmarks drove this: dividing each share by the minor-unit divisor cost roughly 30ns per
    /// recipient and made a 64-way split slower than it needed to be. The integer path applies
    /// whenever the divisor is a power of ten — every currency except MRU and MGA — and whenever the
    /// amount fits in a <see cref="long"/> of minor units, which for DKK means anything below about
    /// 9×10¹⁶. The decimal path remains for the cases it does not cover, so behaviour is unchanged.
    /// </remarks>
    private static bool TryUseIntegerPath(decimal units, long unitsPerMajor, out long integerUnits, out byte scale)
    {
        integerUnits = 0;
        scale = 0;

        if (units < long.MinValue || units > long.MaxValue)
        {
            return false;
        }

        long power = 1;
        byte exponent = 0;

        while (power < unitsPerMajor && exponent < 18)
        {
            power *= 10;
            exponent++;
        }

        if (power != unitsPerMajor)
        {
            // MRU and MGA divide by five, which is not a scale a decimal can carry directly.
            return false;
        }

        integerUnits = (long)units;
        scale = exponent;
        return true;
    }

    /// <summary>Builds a decimal from a whole number of minor units and a scale, without dividing.</summary>
    private static decimal FromMinorUnits(long units, byte scale)
    {
        bool negative = units < 0;
        ulong magnitude = negative ? (ulong)(-(units + 1)) + 1 : (ulong)units;

        return new decimal((int)(magnitude & 0xFFFFFFFF), (int)(magnitude >> 32), 0, negative, scale);
    }

    /// <summary>
    /// Converts the amount into whole minor units, refusing anything that could not be paid out.
    /// </summary>
    private decimal ToMinorUnits(out long unitsPerMajor)
    {
        if (!Currency.IsKnown)
        {
            throw new UnknownCurrencyException(Currency);
        }

        unitsPerMajor = Currency.MinorUnitsPerMajor;

        if (unitsPerMajor == 0)
        {
            // Not an *unknown* currency: XXX and XTS are perfectly well known, they simply have no
            // indivisible unit to distribute. Conflating the two would mislead anyone reading the
            // exception type rather than the message.
            throw new InvalidOperationException(
                $"'{Currency.Code}' has no minor unit, so there is no indivisible amount to allocate in.");
        }

        decimal units = Amount * unitsPerMajor;

        if (decimal.Truncate(units) != units)
        {
            throw new InvalidOperationException(
                $"'{this}' is not a whole number of {Currency.Code} minor units, so no set of payable parts "
              + "can sum to it. Call Round() before allocating.");
        }

        return units;
    }
}
