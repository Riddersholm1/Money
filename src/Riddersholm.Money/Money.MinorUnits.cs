namespace Riddersholm.Money;

/// <content>
/// Conversion to and from a whole number of minor units — the integer form in which payment processors,
/// card networks and ISO 8583 messages carry amounts: <c>1050</c> for <c>10.50 DKK</c>.
/// </content>
/// <remarks>
/// <para>
/// The scale is ISO 4217's exponent, <see cref="Currency.DecimalDigits"/>: hundredths for DKK and EUR,
/// thousandths for KWD, whole units for JPY. That is what every external system means by "minor units",
/// so it is what these methods mean too.
/// </para>
/// <para>
/// For every ISO currency that is also <see cref="Currency.MinorUnit"/>. A currency registered with a
/// divisor that is not a power of ten — fifths, say — still converts at its digit count, because that
/// is the only scale an external system can agree on: <c>1.20</c> is <c>120</c>, not <c>6</c>.
/// </para>
/// </remarks>
public readonly partial record struct Money
{
    /// <summary>Creates an amount from a whole number of the currency's ISO 4217 minor units.</summary>
    /// <param name="minorUnits">The amount in minor units, for example <c>1050</c> for 10.50 DKK.</param>
    /// <param name="currency">The currency, whose <see cref="Currency.DecimalDigits"/> sets the scale.</param>
    /// <returns>The exact amount, carrying the currency's own number of decimal places.</returns>
    /// <example>
    /// <code>
    /// Money.FromMinorUnits(1050, Currency.DKK);   // 10.50 DKK
    /// Money.FromMinorUnits(1050, Currency.JPY);   // 1050 JPY
    /// Money.FromMinorUnits(1050, Currency.KWD);   // 1.050 KWD
    /// </code>
    /// </example>
    /// <exception cref="UnknownCurrencyException">
    /// The currency is not recognised, so its exponent is unknown. Guessing two decimal places would
    /// scale the amount wrongly by a power of ten whenever the guess is wrong.
    /// </exception>
    public static Money FromMinorUnits(long minorUnits, Currency currency)
    {
        if (!currency.IsKnown)
        {
            throw new UnknownCurrencyException(currency);
        }

        return new Money(FromScaledUnits(minorUnits, currency.DecimalDigits), currency);
    }

    /// <summary>The amount as a whole number of the currency's ISO 4217 minor units.</summary>
    /// <returns>The amount scaled by <c>10^</c><see cref="Currency.DecimalDigits"/>, for example <c>1050</c> for 10.50 DKK.</returns>
    /// <remarks>
    /// Never rounds. An amount with more decimal places than the currency has — <c>10.505 DKK</c> — is
    /// refused rather than quietly cut to <c>1050</c>; call <see cref="Round(System.MidpointRounding)"/> first, so
    /// that the rounding is a decision rather than an accident.
    /// </remarks>
    /// <exception cref="UnknownCurrencyException">The currency is not recognised, so its exponent is unknown.</exception>
    /// <exception cref="InvalidOperationException">The amount has more decimal places than the currency.</exception>
    /// <exception cref="OverflowException">The scaled amount does not fit a <see cref="long"/>.</exception>
    public long ToMinorUnits()
    {
        if (!Currency.IsKnown)
        {
            throw new UnknownCurrencyException(Currency);
        }

        decimal scaled = Amount * CurrencyInfo.Pow10(Currency.DecimalDigits);

        if (decimal.Truncate(scaled) != scaled)
        {
            throw new InvalidOperationException(
                $"'{this}' has more decimal places than {Currency.Code}'s {Currency.DecimalDigits}, so it is not a "
              + "whole number of minor units. Call Round() first.");
        }

        return decimal.ToInt64(scaled);
    }

    /// <summary>Converts the amount to a whole number of ISO 4217 minor units, reporting failure rather than throwing.</summary>
    /// <param name="minorUnits">The amount in minor units, or <c>0</c> on failure.</param>
    /// <returns>
    /// <see langword="false"/> if the currency is not recognised, the amount has more decimal places than
    /// the currency, or the result does not fit a <see cref="long"/>.
    /// </returns>
    public bool TryToMinorUnits(out long minorUnits)
    {
        minorUnits = 0;

        if (!Currency.IsKnown)
        {
            return false;
        }

        decimal scaled;

        try
        {
            scaled = Amount * CurrencyInfo.Pow10(Currency.DecimalDigits);
        }
        catch (OverflowException)
        {
            // Only a registered currency with an extreme precision can get here: 10^28 times anything
            // above about eight overflows decimal itself, and that is certainly outside a long.
            return false;
        }

        if (decimal.Truncate(scaled) != scaled || scaled is < long.MinValue or > long.MaxValue)
        {
            return false;
        }

        minorUnits = decimal.ToInt64(scaled);
        return true;
    }
}
