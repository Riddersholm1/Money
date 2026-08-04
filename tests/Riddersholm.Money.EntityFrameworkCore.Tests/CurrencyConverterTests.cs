using Microsoft.EntityFrameworkCore.Storage.ValueConversion;
using Xunit;

namespace Riddersholm.Money.EntityFrameworkCore.Tests;

/// <summary>
/// The value converters, exercised directly rather than through a context, because what matters here
/// is what they do at the boundary — including refusing to write.
/// </summary>
public sealed class CurrencyConverterTests
{
    [Fact]
    public void The_numeric_converter_refuses_to_write_a_currency_that_has_no_numeric_code()
    {
        // Currency.NumericCode is 0 for anything not in the compiled table, and 0 is not an ISO code.
        // Writing it stored a row identifying no currency at all, and the mistake only surfaced when
        // something later read it back — by which point the original code was gone.
        ValueConverter<Currency, short> converter = new CurrencyNumericValueConverter();
        Func<Currency, short> write = converter.ConvertToProviderExpression.Compile();

        var unknown = Currency.FromCode("ZZZ");

        Assert.False(unknown.IsKnown);

        InvalidOperationException error = Assert.Throws<InvalidOperationException>(() => write(unknown));

        Assert.Contains("ZZZ", error.Message, StringComparison.Ordinal);
        Assert.Contains(nameof(CurrencyValueConverter), error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void The_numeric_converter_round_trips_every_known_currency()
    {
        ValueConverter<Currency, short> converter = new CurrencyNumericValueConverter();
        Func<Currency, short> write = converter.ConvertToProviderExpression.Compile();
        Func<short, Currency> read = converter.ConvertFromProviderExpression.Compile();

        foreach (Currency currency in Currency.Known)
        {
            Assert.Equal(currency, read(write(currency)));
        }
    }

    [Fact]
    public void The_alphabetic_converter_round_trips_unknown_currencies_too()
    {
        // The documented reason to prefer it: the alphabetic code is the value, so a currency ISO added
        // after this build survives a round trip untouched.
        ValueConverter<Currency, string> converter = new CurrencyValueConverter();
        Func<Currency, string> write = converter.ConvertToProviderExpression.Compile();
        Func<string, Currency> read = converter.ConvertFromProviderExpression.Compile();

        var unknown = Currency.FromCode("ZZZ");

        Assert.Equal("ZZZ", write(unknown));
        Assert.Equal(unknown, read("ZZZ"));

        foreach (Currency currency in Currency.Known)
        {
            Assert.Equal(currency, read(write(currency)));
        }
    }

    [Fact]
    public void A_column_holding_something_that_is_not_a_currency_names_the_offending_value()
    {
        ValueConverter<Currency, string> converter = new CurrencyValueConverter();
        Func<string, Currency> read = converter.ConvertFromProviderExpression.Compile();

        InvalidOperationException error = Assert.Throws<InvalidOperationException>(() => read("nope"));

        Assert.Contains("nope", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void The_money_text_converter_round_trips_exactly()
    {
        ValueConverter<Money, string> converter = new MoneyValueConverter();
        Func<Money, string> write = converter.ConvertToProviderExpression.Compile();
        Func<string, Money> read = converter.ConvertFromProviderExpression.Compile();

        foreach (Currency currency in Currency.Known)
        {
            foreach (decimal amount in (decimal[])[0m, 1234.56m, -1234.56m, 0.0001m, 123456789.123456789m])
            {
                Money original = new(amount, currency);
                Money restored = read(write(original));

                Assert.Equal(original.Amount, restored.Amount);
                Assert.Equal(original.Currency, restored.Currency);
            }
        }
    }
}
