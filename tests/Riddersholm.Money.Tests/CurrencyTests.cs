using System.Globalization;
using Xunit;

namespace Riddersholm.Money.Tests;

/// <summary>Behaviour of the <see cref="Currency"/> value type itself.</summary>
public sealed class CurrencyTests
{
    [Fact]
    public void Equality_is_by_value()
    {
        Assert.Equal(Currency.DKK, Currency.FromCode("DKK"));
        Assert.NotEqual(Currency.DKK, Currency.EUR);
        Assert.True(Currency.DKK == Currency.FromCode("dkk"));
        Assert.True(Currency.DKK != Currency.NOK);
    }

    [Fact]
    public void Ordering_is_by_alphabetic_code()
    {
        List<Currency> currencies = [Currency.USD, Currency.DKK, Currency.EUR];
        currencies.Sort();

        Assert.Equal([Currency.DKK, Currency.EUR, Currency.USD], currencies);
        Assert.True(Currency.DKK < Currency.EUR);
        Assert.True(Currency.USD > Currency.EUR);
    }

    [Fact]
    public void Unknown_currencies_are_usable_without_metadata()
    {
        var unknown = Currency.FromCode("QQQ");

        Assert.False(unknown.IsKnown);
        Assert.Equal("QQQ", unknown.Code);
        Assert.Equal("QQQ", unknown.EnglishName);
        Assert.Equal("QQQ", unknown.Symbol);
        Assert.Equal(0, unknown.NumericCode);
    }

    [Fact]
    public void Unknown_currency_metadata_is_a_declared_fallback_rather_than_a_throw()
    {
        // Loading unfamiliar data must not crash. The fallback is the ISO default precision, and it
        // announces itself as untrustworthy so that Money.Round can refuse to act on it.
        CurrencyInfo info = Currency.FromCode("QQQ").Info;

        Assert.False(info.IsKnown);
        Assert.Equal(2, info.DecimalDigits);
        Assert.Equal(100, info.MinorUnitsPerMajor);
    }

    [Fact]
    public void Formats_cover_code_number_symbol_and_name()
    {
        Currency dkk = Currency.DKK;

        Assert.Equal("DKK", dkk.ToString());
        Assert.Equal("DKK", dkk.ToString(null, null));
        Assert.Equal("DKK", dkk.ToString("G", CultureInfo.InvariantCulture));
        Assert.Equal("DKK", dkk.ToString("A", CultureInfo.InvariantCulture));
        Assert.Equal("208", dkk.ToString("N", CultureInfo.InvariantCulture));
        Assert.Equal("kr", dkk.ToString("S", CultureInfo.InvariantCulture));
        Assert.Equal("Danish Krone", dkk.ToString("L", CultureInfo.InvariantCulture));
    }

    [Fact]
    public void Numeric_format_is_zero_padded_to_three_digits() =>
        Assert.Equal("008", Currency.ALL.ToString("N", CultureInfo.InvariantCulture));

    [Fact]
    public void Unsupported_format_strings_are_rejected() =>
        Assert.Throws<FormatException>(() => Currency.DKK.ToString("Q", CultureInfo.InvariantCulture));

    [Fact]
    public void TryFormat_writes_the_code_without_allocating_a_string()
    {
        Span<char> buffer = stackalloc char[3];

        Assert.True(Currency.DKK.TryFormat(buffer, out int written));
        Assert.Equal(3, written);
        Assert.True(buffer is "DKK");
    }

    [Fact]
    public void TryFormat_reports_failure_when_the_buffer_is_too_small()
    {
        Span<char> buffer = stackalloc char[2];

        Assert.False(Currency.DKK.TryFormat(buffer, out int written));
        Assert.Equal(0, written);
    }

    [Fact]
    public void TryFormat_writes_utf8_without_transcoding()
    {
        Span<byte> buffer = stackalloc byte[3];

        Assert.True(Currency.DKK.TryFormat(buffer, out int written));
        Assert.Equal(3, written);
        Assert.True(buffer.SequenceEqual("DKK"u8));
    }

    [Fact]
    public void Every_known_currency_formats_and_parses_back()
    {
        Span<char> chars = stackalloc char[3];
        Span<byte> bytes = stackalloc byte[3];

        foreach (Currency currency in Currency.Known)
        {
            Assert.True(currency.TryFormat(chars, out _));
            Assert.True(Currency.TryParse(chars, null, out Currency fromChars));
            Assert.Equal(currency, fromChars);

            Assert.True(currency.TryFormat(bytes, out _));
            Assert.True(Currency.TryParse(bytes, null, out Currency fromBytes));
            Assert.Equal(currency, fromBytes);
        }
    }

    [Theory]
    [InlineData(" DKK ")]
    [InlineData("DKK")]
    [InlineData("\tDKK\n")]
    public void Parsing_tolerates_surrounding_whitespace(string text) =>
        Assert.Equal(Currency.DKK, Currency.Parse(text));

    [Fact]
    public void Parsing_utf8_tolerates_surrounding_whitespace()
    {
        Assert.True(Currency.TryParse(" DKK "u8, null, out Currency currency));
        Assert.Equal(Currency.DKK, currency);
    }

    [Fact]
    public void Parsing_rejects_malformed_input()
    {
        Assert.False(Currency.TryParse("DK", null, out _));
        Assert.False(Currency.TryParse((string?)null, null, out _));
        Assert.Throws<FormatException>(() => Currency.Parse("nonsense"));
    }

    [Fact]
    public void Minor_unit_reflects_the_currency_precision()
    {
        Assert.Equal(0.01m, Currency.DKK.MinorUnit);
        Assert.Equal(1m, Currency.JPY.MinorUnit);
        Assert.Equal(0.001m, Currency.KWD.MinorUnit);
    }
}
