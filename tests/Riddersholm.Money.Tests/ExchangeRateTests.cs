using Xunit;

namespace Riddersholm.Money.Tests;

public sealed class ExchangeRateTests
{
    private static readonly ExchangeRate DkkToEur = new(Currency.DKK, Currency.EUR, 0.134m);

    [Fact]
    public void Conversion_produces_the_quote_currency()
    {
        Money euros = DkkToEur.Convert(new Money(100m, Currency.DKK));

        Assert.Equal(Currency.EUR, euros.Currency);
        Assert.Equal(13.4m, euros.Amount);
    }

    [Fact]
    public void Conversion_does_not_round()
    {
        // Consistent with the rest of the library: the caller decides where rounding belongs.
        Money euros = DkkToEur.Convert(new Money(1m, Currency.DKK));

        Assert.Equal(0.134m, euros.Amount);
        Assert.Equal(0.13m, euros.Round().Amount);
    }

    [Fact]
    public void Converting_the_wrong_currency_is_refused()
    {
        CurrencyMismatchException error = Assert.Throws<CurrencyMismatchException>(
            () => DkkToEur.Convert(new Money(100m, Currency.USD)));

        Assert.Equal(Currency.USD, error.Left);
        Assert.Equal(Currency.DKK, error.Right);
    }

    [Fact]
    public void Converting_back_returns_the_base_currency()
    {
        Money kroner = DkkToEur.ConvertBack(new Money(13.4m, Currency.EUR));

        Assert.Equal(Currency.DKK, kroner.Currency);
        Assert.Equal(100m, kroner.Amount);
        Assert.Throws<CurrencyMismatchException>(() => DkkToEur.ConvertBack(new Money(1m, Currency.DKK)));
    }

    [Fact]
    public void Inverting_swaps_the_currencies()
    {
        ExchangeRate inverted = DkkToEur.Invert();

        Assert.Equal(Currency.EUR, inverted.BaseCurrency);
        Assert.Equal(Currency.DKK, inverted.QuoteCurrency);
        Assert.Equal(1m / 0.134m, inverted.Rate);
    }

    [Fact]
    public void Non_positive_rates_are_rejected()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new ExchangeRate(Currency.DKK, Currency.EUR, 0m));
        Assert.Throws<ArgumentOutOfRangeException>(() => new ExchangeRate(Currency.DKK, Currency.EUR, -1m));
    }

    [Fact]
    public void Rates_have_value_equality()
    {
        Assert.Equal(DkkToEur, new ExchangeRate(Currency.DKK, Currency.EUR, 0.134m));
        Assert.NotEqual(DkkToEur, new ExchangeRate(Currency.DKK, Currency.EUR, 0.135m));
    }

    [Fact]
    public void ToString_uses_the_conventional_pair_notation() =>
        Assert.Equal("DKK/EUR 0.134", DkkToEur.ToString());
}
