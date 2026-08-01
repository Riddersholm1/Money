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
/// Conversion is exact and does not round; call <see cref="Money.Round(System.MidpointRounding)"/> when you have decided
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
    /// <exception cref="ArgumentException">
    /// <paramref name="baseCurrency"/> and <paramref name="quoteCurrency"/> are the same currency but
    /// <paramref name="rate"/> is not <c>1</c>. A currency does not trade against itself at anything
    /// else, so such a rate is a data error that would otherwise create or destroy money silently.
    /// </exception>
    public ExchangeRate(Currency baseCurrency, Currency quoteCurrency, decimal rate)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(rate);

        if (baseCurrency == quoteCurrency && rate != 1m)
        {
            throw new ArgumentException(
                $"A {baseCurrency.Code}/{quoteCurrency.Code} rate must be 1, but was {rate.ToString(System.Globalization.CultureInfo.InvariantCulture)}. "
              + "Converting a currency to itself at any other rate creates or destroys money.",
                nameof(rate));
        }

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

    /// <summary>
    /// Whether this value came from the constructor rather than being <c>default(ExchangeRate)</c>.
    /// </summary>
    /// <remarks>
    /// Every struct has a default that no constructor guards, and for this type that default carries a
    /// <see cref="Rate"/> of zero — a value the constructor explicitly refuses. Multiplying by it would
    /// turn any amount into zero, so <see cref="Convert"/> and <see cref="ConvertBack"/> check this
    /// first. A <c>default(ExchangeRate)</c> can arrive from <c>new ExchangeRate[n]</c>, an unassigned
    /// field, a <c>default</c> switch arm, or deserialisation of an absent value.
    /// </remarks>
    public bool IsSpecified => Rate != 0m;

    /// <summary>Converts an amount from <see cref="BaseCurrency"/> to <see cref="QuoteCurrency"/>.</summary>
    /// <param name="amount">The amount to convert. Its currency must be <see cref="BaseCurrency"/>.</param>
    /// <returns>The converted amount, exact and unrounded.</returns>
    /// <exception cref="CurrencyMismatchException"><paramref name="amount"/> is not in <see cref="BaseCurrency"/>.</exception>
    /// <exception cref="InvalidOperationException">This is <c>default(ExchangeRate)</c> — see <see cref="IsSpecified"/>.</exception>
    public Money Convert(Money amount)
    {
        EnsureSpecified();

        return amount.Currency == BaseCurrency
            ? new Money(amount.Amount * Rate, QuoteCurrency)
            : throw new CurrencyMismatchException(amount.Currency, BaseCurrency);
    }

    /// <summary>Converts an amount from <see cref="QuoteCurrency"/> back to <see cref="BaseCurrency"/>.</summary>
    /// <param name="amount">The amount to convert. Its currency must be <see cref="QuoteCurrency"/>.</param>
    /// <returns>The converted amount, exact and unrounded.</returns>
    /// <exception cref="CurrencyMismatchException"><paramref name="amount"/> is not in <see cref="QuoteCurrency"/>.</exception>
    /// <exception cref="InvalidOperationException">This is <c>default(ExchangeRate)</c> — see <see cref="IsSpecified"/>.</exception>
    public Money ConvertBack(Money amount)
    {
        EnsureSpecified();

        return amount.Currency == QuoteCurrency
            ? new Money(amount.Amount / Rate, BaseCurrency)
            : throw new CurrencyMismatchException(amount.Currency, QuoteCurrency);
    }

    /// <summary>
    /// Refuses to act on an uninitialised rate.
    /// </summary>
    /// <remarks>
    /// Both directions check, and both throw the same way. Before this existed they disagreed:
    /// <see cref="Convert"/> multiplied by zero and returned a silent zero, while
    /// <see cref="ConvertBack"/> divided by zero and threw. A value that destroys money in one
    /// direction and throws in the other gives nothing a chance to notice it is wrong.
    /// </remarks>
    private void EnsureSpecified()
    {
        if (!IsSpecified)
        {
            throw new InvalidOperationException(
                "This ExchangeRate is default(ExchangeRate) and carries no rate. Construct it with "
              + "new ExchangeRate(baseCurrency, quoteCurrency, rate) before converting.");
        }
    }

    /// <summary>The same rate expressed in the opposite direction.</summary>
    /// <remarks>
    /// The inverted rate is <c>1 / </c><see cref="Rate"/>, which is rarely exact in decimal, so
    /// <c>rate.Invert().Invert()</c> need not equal <c>rate</c>. Prefer <see cref="ConvertBack"/> when
    /// you simply want to convert the other way.
    /// </remarks>
    /// <exception cref="InvalidOperationException">This is <c>default(ExchangeRate)</c> — see <see cref="IsSpecified"/>.</exception>
    public ExchangeRate Invert()
    {
        EnsureSpecified();

        return new ExchangeRate(QuoteCurrency, BaseCurrency, 1m / Rate);
    }

    /// <summary>Returns the rate in the conventional <c>BASE/QUOTE rate</c> form.</summary>
    public override string ToString() =>
        string.Create(System.Globalization.CultureInfo.InvariantCulture, $"{BaseCurrency.Code}/{QuoteCurrency.Code} {Rate}");
}
