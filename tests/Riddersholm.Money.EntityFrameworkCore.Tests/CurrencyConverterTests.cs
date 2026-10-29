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
            // A withdrawn currency whose number ISO reused is refused instead; see the next test.
            if (Currency.TryFromNumericCode(currency.NumericCode, out Currency owner) && owner == currency)
            {
                Assert.Equal(currency, read(write(currency)));
            }
        }
    }

    [Fact]
    public void The_numeric_converter_refuses_a_currency_whose_number_now_belongs_to_another()
    {
        // ISO gave ANG's 532 to XCG. Stored as 532, ANG would come back as XCG — a different currency
        // on the next read, with nothing to say it happened.
        Func<Currency, short> write = new CurrencyNumericValueConverter().ConvertToProviderExpression.Compile();

        InvalidOperationException error = Assert.Throws<InvalidOperationException>(() => write(Currency.FromCode("ANG")));

        Assert.Contains("XCG", error.Message, StringComparison.Ordinal);

        // A withdrawn currency whose number nobody reused still stores fine.
        Assert.Equal((short)975, write(Currency.FromCode("BGN")));
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

    [Theory]
    [InlineData("1,5 DKK")]     // a European one-and-a-half, which the lenient styles read as fifteen
    [InlineData("1,000.50 DKK")]
    [InlineData("(5) DKK")]
    [InlineData("kr. 5")]
    public void The_money_text_converter_refuses_text_it_did_not_write(string stored)
    {
        Func<string, Money> read = new MoneyValueConverter().ConvertFromProviderExpression.Compile();

        InvalidOperationException error = Assert.Throws<InvalidOperationException>(() => read(stored));

        Assert.Contains(stored, error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void The_money_text_converter_tolerates_fixed_length_padding()
    {
        Func<string, Money> read = new MoneyValueConverter().ConvertFromProviderExpression.Compile();

        Assert.Equal(new Money(100.5m, Currency.DKK), read("100.50 DKK      "));
    }
}
