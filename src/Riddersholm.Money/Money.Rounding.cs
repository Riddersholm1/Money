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

            byte digits = Currency.DecimalDigits;

            // The common case never multiplies, so it cannot overflow: an amount is representable
            // exactly when rounding it to the currency's digit count leaves it unchanged.
            if (units == Pow10Long(digits))
            {
                return Math.Round(Amount, digits, MidpointRounding.ToZero) == Amount;
            }

            // MRU and MGA divide by five, which no digit count expresses, so the increment has to be
            // applied directly. Overflow is possible in principle for an astronomically large amount;
            // a property must answer rather than throw, and an amount that cannot even be scaled is
            // certainly not a whole number of minor units.
            try
            {
                decimal minorUnits = Amount * units;
                return decimal.Truncate(minorUnits) == minorUnits;
            }
            catch (OverflowException)
            {
                return false;
            }
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
    /// Rounds to the currency's <em>increment</em>, not merely to a digit count, so MRU and MGA snap to
    /// multiples of <c>0.2</c> rather than <c>0.01</c>. Currencies with no minor unit are returned
    /// unchanged.
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

        return new Money(RoundToUnits(Amount, units, Currency.DecimalDigits, mode), Currency);
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
    /// </remarks>
    /// <exception cref="UnknownCurrencyException">The currency is not recognised.</exception>
    public Money RoundToCash(MidpointRounding mode = MidpointRounding.ToEven)
    {
        if (!Currency.IsKnown)
        {
            throw new UnknownCurrencyException(Currency);
        }

        if (Currency.MinorUnitsPerMajor == 0)
        {
            return this;
        }

        byte digits = Currency.CashDecimalDigits;
        byte step = Currency.CashRoundingStep;

        if (step == 1)
        {
            return new Money(Math.Round(Amount, digits, mode), Currency);
        }

        // The step is counted in last-place units, so CHF's step of 5 at 2 digits means 0.05.
        decimal increment = step / Pow10(digits);
        return new Money(Math.Round(Amount / increment, 0, mode) * increment, Currency);
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

    private static decimal RoundToUnits(decimal amount, long unitsPerMajor, byte digits, MidpointRounding mode)
    {
        // The overwhelmingly common case is a power of ten, where decimal.Round is both exact and
        // cheaper than scaling by hand — and cannot overflow the way a multiply by 10^18 could.
        if (unitsPerMajor == Pow10Long(digits))
        {
            return Math.Round(amount, digits, mode);
        }

        // MRU and MGA: a fifth of the major unit. Scale into whole minor units, round, and scale back.
        return Math.Round(amount * unitsPerMajor, 0, mode) / unitsPerMajor;
    }

    private static decimal Pow10(int exponent)
    {
        decimal result = 1m;
        for (int i = 0; i < exponent; i++)
        {
            result *= 10m;
        }

        return result;
    }

    /// <summary>
    /// Ten to the power of <paramref name="exponent"/>, or <c>-1</c> when that does not fit a
    /// <see cref="long"/>.
    /// </summary>
    /// <remarks>
    /// Returning a sentinel rather than saturating at 10^18 matters. Callers use this to decide whether
    /// the minor-unit divisor is a power of ten, and a saturating version answered "yes" for every
    /// precision above 18 — so a currency registered with 28 digits and a 10^18 divisor was rounded to
    /// 28 decimal places when its real increment is 10⁻¹⁸. An impossible value can never compare equal
    /// to a real divisor, so the general path is taken instead, which is correct for any divisor.
    /// </remarks>
    internal static long Pow10Long(int exponent)
    {
        if (exponent is < 0 or > 18)
        {
            return -1;
        }

        long result = 1;
        for (int i = 0; i < exponent; i++)
        {
            result *= 10;
        }

        return result;
    }
}
