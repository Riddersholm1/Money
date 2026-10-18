namespace Riddersholm.Money;

/// <content>
/// Rounding. Nothing else in the library rounds; every rounding decision is made here, by an explicit
/// call.
/// </content>
public readonly partial record struct Money
{
    /// <summary>
    /// Whether the amount is representable in its currency — a whole number of minor units.
    /// </summary>
    /// <remarks>
    /// <c>100.00 DKK</c> and <c>100 DKK</c> are canonical; <c>100.005 DKK</c> is not. Currencies with
    /// no minor unit (<c>XXX</c>, <c>XTS</c>) are always canonical, since there is no increment to
    /// violate.
    /// <para>
    /// For a currency the library does not recognise this answers using the ISO default of two
    /// decimals, which is a guess — check <see cref="Currency.IsKnown"/> first if that matters. Reading
    /// a best-effort answer is safe; <see cref="Round(System.MidpointRounding)"/> refuses to <em>change</em> money on a guess.
    /// </para>
    /// </remarks>
    public bool IsCanonical
    {
        get
        {
            long units = Currency.MinorUnitsPerMajor;

            if (units == 0)
            {
                return true;
            }

            // The common case never multiplies, so it cannot overflow: an amount is representable
            // exactly when rounding it to the minor unit's decimal places leaves it unchanged.
            if (TryGetDecimalPlaces(units, out int places))
            {
                return Math.Round(Amount, places, MidpointRounding.ToZero) == Amount;
            }

            // A registered currency divided into fifths has an increment no digit count expresses, so
            // it is applied directly — and exactly. Multiplying in decimal is not enough:
            // 3.9999999999999999999999999999 times five needs a 30th digit, decimal rounds the product
            // to a whole 20, and the amount would be reported as a whole number of fifths when it is not.
            return IsWholeMultiple(Amount, 1, units);
        }
    }

    /// <summary>Rounds to the currency's minor unit.</summary>
    /// <param name="mode">
    /// How to resolve values that fall between two representable amounts. The default,
    /// <see cref="MidpointRounding.ToEven"/>, matches <see cref="decimal.Round(decimal)"/> and is
    /// statistically neutral over many roundings; several tax regimes instead require
    /// <see cref="MidpointRounding.AwayFromZero"/>.
    /// </param>
    /// <remarks>
    /// Rounds to the currency's <em>increment</em>, not merely to a digit count, so a registered currency
    /// divided into fifths snaps to multiples of <c>0.2</c> rather than <c>0.01</c>. Currencies with no
    /// minor unit are returned unchanged.
    /// </remarks>
    /// <exception cref="UnknownCurrencyException">
    /// The currency is not recognised, so its precision is a guess. Rounding to a guessed precision
    /// silently alters money; use <see cref="Round(int, MidpointRounding)"/> to state the precision.
    /// </exception>
    public Money Round(MidpointRounding mode = MidpointRounding.ToEven)
    {
        if (!Currency.IsKnown)
        {
            throw new UnknownCurrencyException(Currency);
        }

        long units = Currency.MinorUnitsPerMajor;

        if (units == 0)
        {
            // XXX and XTS have no minor unit; there is no increment to snap to.
            return this;
        }

        return new Money(RoundToUnits(Amount, units, mode), Currency);
    }

    /// <summary>Rounds to an explicit number of decimal places, regardless of the currency.</summary>
    /// <param name="decimals">The number of decimal places, <c>0</c> to <c>28</c>.</param>
    /// <param name="mode">How to resolve values that fall exactly between two results.</param>
    /// <remarks>Works for any currency, including ones the library does not recognise.</remarks>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="decimals"/> is outside <c>0..28</c>.</exception>
    public Money Round(int decimals, MidpointRounding mode = MidpointRounding.ToEven)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(decimals);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(decimals, 28);

        return new Money(Math.Round(Amount, decimals, mode), Currency);
    }

    /// <summary>Rounds to the smallest amount that can actually be paid in cash.</summary>
    /// <param name="mode">How to resolve values that fall exactly between two payable amounts.</param>
    /// <remarks>
    /// Cash precision is coarser than accounting precision wherever small coins have been withdrawn:
    /// Swiss cash rounds to <c>0.05</c>, Danish cash to <c>0.50</c>, Hungarian cash to <c>5</c> forint.
    /// Ledgers want <see cref="Round(System.MidpointRounding)"/>; tills want this.
    /// <para>
    /// The result is always <see cref="IsCanonical">canonical</see>: cash rounding can only ever be
    /// coarser than the currency's own increment, never finer.
    /// </para>
    /// </remarks>
    /// <exception cref="UnknownCurrencyException">The currency is not recognised.</exception>
    public Money RoundToCash(MidpointRounding mode = MidpointRounding.ToEven)
    {
        if (!Currency.IsKnown)
        {
            throw new UnknownCurrencyException(Currency);
        }

        long unitsPerMajor = Currency.MinorUnitsPerMajor;

        if (unitsPerMajor == 0)
        {
            // XXX and XTS have no minor unit, so there is nothing to snap to.
            return this;
        }

        // The cash increment, counted in minor units: the step is in last-place units of the cash
        // digit count, so CHF's step of 5 at 2 digits is 0.05, which is 5 rappen.
        decimal cashUnits = (decimal)Currency.CashRoundingStep * unitsPerMajor / CurrencyInfo.Pow10(Currency.CashDecimalDigits);

        // A cash increment below one minor unit — or not a whole number of them — would round to an
        // amount the currency cannot express — a currency registered in fifths with two cash decimals
        // would yield 0.01, which nobody can hold. Where the two rules disagree the currency's own
        // increment wins, because a cash amount that is not a valid accounting amount is not payable
        // either.
        if (cashUnits <= 1m || decimal.Truncate(cashUnits) != cashUnits)
        {
            return new Money(RoundToUnits(Amount, unitsPerMajor, mode), Currency);
        }

        // The increment in major units is cashUnits / unitsPerMajor. Reduced to lowest terms it is
        // small on both sides — ½ for DKK, ¹⁄₂₀ for CHF, 5 for HUF — which keeps the exact arithmetic
        // below in 128-bit integers.
        var numerator = (UInt128)cashUnits;
        UInt128 divisor = GreatestCommonDivisor(numerator, (UInt128)unitsPerMajor);

        return new Money(
            RoundToIncrement(Amount, numerator / divisor, (UInt128)unitsPerMajor / divisor, mode),
            Currency);
    }

    /// <summary>Rounds toward negative infinity, to the currency's minor unit.</summary>
    /// <exception cref="UnknownCurrencyException">The currency is not recognised.</exception>
    public Money Floor() => Round(MidpointRounding.ToNegativeInfinity);

    /// <summary>Rounds toward positive infinity, to the currency's minor unit.</summary>
    /// <exception cref="UnknownCurrencyException">The currency is not recognised.</exception>
    public Money Ceiling() => Round(MidpointRounding.ToPositiveInfinity);

    /// <summary>Discards the fractional minor units, rounding toward zero.</summary>
    /// <exception cref="UnknownCurrencyException">The currency is not recognised.</exception>
    public Money Truncate() => Round(MidpointRounding.ToZero);

    private static decimal RoundToUnits(decimal amount, long unitsPerMajor, MidpointRounding mode)
    {
        // The overwhelmingly common case is a power of ten, where decimal.Round is exact, cheap, and
        // cannot overflow. The exponent comes from the divisor itself rather than from DecimalDigits:
        // a currency registered with 28 digits and a 10^18 divisor steps by 10^-18, and asking
        // decimal.Round for 18 places is what keeps a large amount from overflowing on the way.
        if (TryGetDecimalPlaces(unitsPerMajor, out int places))
        {
            return Math.Round(amount, places, mode);
        }

        // A divisor that is not a power of ten — a registered currency divided into fifths, say.
        return RoundToIncrement(amount, UInt128.One, (UInt128)unitsPerMajor, mode);
    }

    /// <summary>Whether <paramref name="unitsPerMajor"/> is ten to some power, and which.</summary>
    /// <remarks>
    /// Asked of the divisor rather than derived from the digit count, which is what lets a single test
    /// cover every currency correctly. An earlier version compared the divisor against a power of ten
    /// computed from <see cref="Currency.DecimalDigits"/>, and that power saturated at 10^18, so every
    /// precision above 18 looked like a match and was rounded to the wrong number of places.
    /// </remarks>
    internal static bool TryGetDecimalPlaces(long unitsPerMajor, out int places)
    {
        places = 0;

        if (unitsPerMajor <= 0)
        {
            return false;
        }

        while (unitsPerMajor % 10 == 0)
        {
            unitsPerMajor /= 10;
            places++;
        }

        return unitsPerMajor == 1;
    }

    /// <summary>
    /// Rounds <paramref name="amount"/> to a whole multiple of
    /// <paramref name="incrementNumerator"/> ⁄ <paramref name="incrementDenominator"/>, exactly.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Done in integers because decimal cannot do it reliably. The obvious form —
    /// <c>Math.Round(amount * 5) / 5</c> — rounds twice: once when the product needs more digits than
    /// decimal carries, and only then by the requested mode. For an ordinary amount the first rounding
    /// never happens, but for one carrying 28 significant digits it does, and it can run the wrong way:
    /// <c>Floor</c> of <c>3.9999999999999999999999999999</c> in fifths came back as <c>4.0</c>, above the
    /// input. It also overflowed outright for amounts near the top of decimal's range.
    /// </para>
    /// <para>
    /// An amount is <c>±mantissa / 10^scale</c>, so the number of increments it holds is the fraction
    /// <c>mantissa·d / (10^scale·n)</c>. Its integer part and remainder are exact, and the remainder alone
    /// decides the rounding, the same way <see cref="Math.Round(decimal, int, MidpointRounding)"/> does
    /// for a power of ten.
    /// </para>
    /// </remarks>
    private static decimal RoundToIncrement(decimal amount, UInt128 incrementNumerator, UInt128 incrementDenominator, MidpointRounding mode)
    {
        (UInt128 mantissa, UInt128 power, bool negative) = Decompose(amount);

        // Both products fit 128 bits when the increment's terms fit 32: the mantissa is below 2^96 and
        // the power of ten below 2^94. Every currency the library ships, and any sane registration,
        // takes this path; BigInteger is the fallback that makes the rest correct too.
        if (incrementNumerator > uint.MaxValue || incrementDenominator > uint.MaxValue)
        {
            return RoundToIncrementWide(mantissa, power, negative, incrementNumerator, incrementDenominator, mode);
        }

        UInt128 divisor = power * incrementNumerator;
        (UInt128 increments, UInt128 remainder) = UInt128.DivRem(mantissa * incrementDenominator, divisor);

        increments = RoundQuotient(increments, remainder << 1, divisor, negative, mode);
        return FromIncrements(increments * incrementNumerator, incrementDenominator, negative);
    }

    private static decimal RoundToIncrementWide(
        UInt128 mantissa,
        UInt128 power,
        bool negative,
        UInt128 incrementNumerator,
        UInt128 incrementDenominator,
        MidpointRounding mode)
    {
        var denominator = (System.Numerics.BigInteger)power * incrementNumerator;
        var increments = System.Numerics.BigInteger.DivRem(
            (System.Numerics.BigInteger)mantissa * incrementDenominator,
            denominator,
            out System.Numerics.BigInteger remainder);

        increments = RoundQuotient(increments, remainder << 1, denominator, negative, mode);

        System.Numerics.BigInteger total = increments * incrementNumerator;
        var whole = System.Numerics.BigInteger.DivRem(total, incrementDenominator, out System.Numerics.BigInteger fraction);

        decimal magnitude = (decimal)whole + ((decimal)fraction / (decimal)incrementDenominator);
        return negative ? -magnitude : magnitude;
    }

    /// <summary>
    /// Whether <paramref name="amount"/> is an exact whole multiple of
    /// <paramref name="incrementNumerator"/> ⁄ <paramref name="incrementDenominator"/>.
    /// </summary>
    private static bool IsWholeMultiple(decimal amount, long incrementNumerator, long incrementDenominator)
    {
        (UInt128 mantissa, UInt128 power, _) = Decompose(amount);

        if (incrementNumerator <= uint.MaxValue && incrementDenominator <= uint.MaxValue)
        {
            return mantissa * (ulong)incrementDenominator % (power * (ulong)incrementNumerator) == UInt128.Zero;
        }

        return ((System.Numerics.BigInteger)mantissa * incrementDenominator
            % ((System.Numerics.BigInteger)power * incrementNumerator)).IsZero;
    }

    /// <summary>
    /// Applies <paramref name="mode"/> to a quotient whose exact remainder is known, the way
    /// <see cref="Math.Round(decimal, int, MidpointRounding)"/> treats the digits it discards.
    /// </summary>
    /// <param name="quotient">The magnitude's quotient, truncated.</param>
    /// <param name="remainderTimesTwo">Twice the remainder, so a midpoint compares equal to the divisor.</param>
    /// <param name="divisor">The divisor the remainder is out of.</param>
    /// <param name="negative">Whether the value being rounded is negative.</param>
    /// <param name="mode">The rounding rule.</param>
    private static T RoundQuotient<T>(T quotient, T remainderTimesTwo, T divisor, bool negative, MidpointRounding mode)
        where T : System.Numerics.IBinaryInteger<T>
    {
        // Validated before the early return, as Math.Round does, so an invalid mode fails the same way
        // whether or not this particular amount needed rounding.
        if (mode is < MidpointRounding.ToEven or > MidpointRounding.ToPositiveInfinity)
        {
            throw new ArgumentException($"The value '{mode}' is not a valid {nameof(MidpointRounding)}.", nameof(mode));
        }

        if (T.IsZero(remainderTimesTwo))
        {
            return quotient;
        }

        int midpoint = remainderTimesTwo.CompareTo(divisor);

        bool awayFromZero = mode switch
        {
            MidpointRounding.ToEven => midpoint > 0 || (midpoint == 0 && T.IsOddInteger(quotient)),
            MidpointRounding.AwayFromZero => midpoint >= 0,
            MidpointRounding.ToZero => false,
            MidpointRounding.ToNegativeInfinity => negative,
            _ => !negative
        };

        return awayFromZero ? quotient + T.One : quotient;
    }

    /// <summary>
    /// Builds <c>±total ⁄ denominator</c> as a decimal, splitting off the whole part so the conversion
    /// never needs more digits than the result has.
    /// </summary>
    /// <remarks>
    /// The fraction is exact because every divisor reaching here divides a power of ten — the
    /// registration check in <see cref="CurrencyInfo"/> guarantees it for the minor unit, and a cash
    /// increment is a whole number of minor units.
    /// </remarks>
    private static decimal FromIncrements(UInt128 total, UInt128 denominator, bool negative)
    {
        (UInt128 whole, UInt128 fraction) = UInt128.DivRem(total, denominator);

        decimal magnitude = (decimal)whole + ((decimal)fraction / (decimal)denominator);

        // Never hand back a negative zero: it compares equal to zero but is a distinct bit pattern, and
        // nothing about rounding -0.001 should leave a sign behind.
        return negative && magnitude != 0m ? -magnitude : magnitude;
    }

    /// <summary>Splits a decimal into its unsigned mantissa, ten to the power of its scale, and its sign.</summary>
    private static (UInt128 Mantissa, UInt128 Power, bool Negative) Decompose(decimal value)
    {
        Span<int> bits = stackalloc int[4];
        decimal.GetBits(value, bits);

        UInt128 mantissa = ((UInt128)(uint)bits[2] << 64) | ((UInt128)(uint)bits[1] << 32) | (uint)bits[0];
        int scale = (bits[3] >> 16) & 0xFF;

        UInt128 power = UInt128.One;
        for (int i = 0; i < scale; i++)
        {
            power *= 10u;
        }

        return (mantissa, power, bits[3] < 0);
    }

    private static UInt128 GreatestCommonDivisor(UInt128 left, UInt128 right)
    {
        while (right != UInt128.Zero)
        {
            (left, right) = (right, left % right);
        }

        return left;
    }
}
