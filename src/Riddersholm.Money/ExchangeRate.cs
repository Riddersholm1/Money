namespace Riddersholm.Money;

/// <summary>
/// A rate for converting between two currencies.
/// </summary>
/// <remarks>
/// <para>
/// Cross-currency arithmetic is refused precisely because it needs a rate, and a rate is a fact about
/// the world at a moment in time rather than a property of an amount. This type makes the conversion
/// possible and, more importantly, makes it visible: every conversion names the rate that produced it.
/// </para>
/// <para>
/// <b>Fetching rates is out of scope.</b> Where the number comes from — a central bank feed, a
/// contract, yesterday's close — and how stale it is allowed to be are application concerns with
/// auditing implications. This type only applies a rate you supply.
/// </para>
/// <para>
/// Conversion is exact and does not round; call <see cref="Money.Round()"/> when you have decided
/// where rounding belongs.
/// </para>
/// </remarks>
/// <example>
/// <code>
/// var rate = new ExchangeRate(Currency.DKK, Currency.EUR, 0.134m);
/// Money euros = rate.Convert(new Money(100m, Currency.DKK)).Round();   // 13.40 EUR
/// </code>
/// </example>
public readonly record struct ExchangeRate
{
    /// <summary>Creates a rate for converting from one currency to another.</summary>
    /// <param name="baseCurrency">The currency being converted from.</param>
    /// <param name="quoteCurrency">The currency being converted to.</param>
    /// <param name="rate">How many units of <paramref name="quoteCurrency"/> one unit of <paramref name="baseCurrency"/> buys.</param>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="rate"/> is not greater than zero.</exception>
    public ExchangeRate(Currency baseCurrency, Currency quoteCurrency, decimal rate)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(rate);

        BaseCurrency = baseCurrency;
        QuoteCurrency = quoteCurrency;
        Rate = rate;
    }

    /// <summary>The currency being converted from.</summary>
    public Currency BaseCurrency { get; }

    /// <summary>The currency being converted to.</summary>
    public Currency QuoteCurrency { get; }

    /// <summary>How many units of <see cref="QuoteCurrency"/> one unit of <see cref="BaseCurrency"/> buys.</summary>
    public decimal Rate { get; }

    /// <summary>Converts an amount from <see cref="BaseCurrency"/> to <see cref="QuoteCurrency"/>.</summary>
    /// <param name="amount">The amount to convert. Its currency must be <see cref="BaseCurrency"/>.</param>
    /// <returns>The converted amount, exact and unrounded.</returns>
    /// <exception cref="CurrencyMismatchException"><paramref name="amount"/> is not in <see cref="BaseCurrency"/>.</exception>
    public Money Convert(Money amount) =>
        amount.Currency == BaseCurrency
            ? new Money(amount.Amount * Rate, QuoteCurrency)
            : throw new CurrencyMismatchException(amount.Currency, BaseCurrency);

    /// <summary>Converts an amount from <see cref="QuoteCurrency"/> back to <see cref="BaseCurrency"/>.</summary>
    /// <param name="amount">The amount to convert. Its currency must be <see cref="QuoteCurrency"/>.</param>
    /// <returns>The converted amount, exact and unrounded.</returns>
    /// <exception cref="CurrencyMismatchException"><paramref name="amount"/> is not in <see cref="QuoteCurrency"/>.</exception>
    public Money ConvertBack(Money amount) =>
        amount.Currency == QuoteCurrency
            ? new Money(amount.Amount / Rate, BaseCurrency)
            : throw new CurrencyMismatchException(amount.Currency, QuoteCurrency);

    /// <summary>The same rate expressed in the opposite direction.</summary>
    /// <remarks>
    /// The inverted rate is <c>1 / </c><see cref="Rate"/>, which is rarely exact in decimal, so
    /// <c>rate.Invert().Invert()</c> need not equal <c>rate</c>. Prefer <see cref="ConvertBack"/> when
    /// you simply want to convert the other way.
    /// </remarks>
    public ExchangeRate Invert() => new(QuoteCurrency, BaseCurrency, 1m / Rate);

    /// <summary>Returns the rate in the conventional <c>BASE/QUOTE rate</c> form.</summary>
    public override string ToString() =>
        string.Create(System.Globalization.CultureInfo.InvariantCulture, $"{BaseCurrency.Code}/{QuoteCurrency.Code} {Rate}");
}
