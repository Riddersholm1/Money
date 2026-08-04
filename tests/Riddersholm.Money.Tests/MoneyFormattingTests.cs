using System.Globalization;
using System.Text;
using Xunit;

namespace Riddersholm.Money.Tests;

public sealed class MoneyFormattingTests
{
    private static readonly CultureInfo Invariant = CultureInfo.InvariantCulture;
    private static readonly CultureInfo Danish = new("da-DK");
    private static readonly CultureInfo American = new("en-US");

    private static Money Dkk(decimal amount) => new(amount, Currency.DKK);

    [Fact]
    public void General_format_is_amount_then_iso_code()
    {
        Assert.Equal("100.00 DKK", Dkk(100m).ToString(null, Invariant));
        Assert.Equal("100.00 DKK", Dkk(100m).ToString("G", Invariant));
        Assert.Equal("100.50 DKK", Dkk(100.50m).ToString("G", Invariant));
    }

    [Fact]
    public void Iso_format_puts_the_code_first() =>
        Assert.Equal("DKK 100.00", Dkk(100m).ToString("I", Invariant));

    [Fact]
    public void Currency_format_uses_the_symbol_and_the_cultures_layout()
    {
        // Danish puts the symbol last and uses a comma; American puts it first and uses a point.
        Assert.Equal("100,50 kr.", Dkk(100.50m).ToString("C", Danish));
        Assert.Equal("kr1,234.50", new Money(1234.50m, Currency.DKK).ToString("C", American));
    }

    [Fact]
    public void Currency_format_uses_the_currencies_precision_not_the_cultures()
    {
        // The BCL always uses the culture's CurrencyDecimalDigits, so decimal.ToString("C", en-US) is
        // $1,234.00 whatever currency was meant. Yen has no minor unit and dinars have three.
        Assert.Equal("¥1,234", new Money(1234m, Currency.JPY).ToString("C", American));
        Assert.Equal("KWD1.234", new Money(1.234m, Currency.KWD).ToString("C", American));
        Assert.Equal("$1,234.00", new Money(1234m, Currency.USD).ToString("C", American));
    }

    [Fact]
    public void Currency_format_prefers_the_cultures_own_symbol_when_the_region_matches()
    {
        // Danes write "kr." with a full stop; the culture-neutral CLDR narrow form is "kr".
        Assert.Equal("100,50 kr.", Dkk(100.50m).ToString("C", Danish));
        Assert.Equal("kr100.50", Dkk(100.50m).ToString("C", American));
    }

    [Fact]
    public void Number_format_omits_the_currency_and_groups_digits() =>
        Assert.Equal("1,234,567.89", new Money(1234567.89m, Currency.USD).ToString("N", American));

    [Fact]
    public void Name_format_spells_out_the_currency() =>
        Assert.Equal("100.00 Danish Krone", Dkk(100m).ToString("L", Invariant));

    [Fact]
    public void Round_trip_format_ignores_the_provider()
    {
        // 'R' must be readable by Parse under the invariant culture, so a Danish comma would break it.
        Assert.Equal("100.50 DKK", Dkk(100.50m).ToString("R", Danish));
        Assert.Equal("100.50 DKK", Dkk(100.50m).ToString("R", Invariant));
    }

    [Fact]
    public void Round_trip_format_rejects_a_digit_count() =>
        Assert.Throws<FormatException>(() => Dkk(100.505m).ToString("R2", Invariant));

    [Fact]
    public void Formatting_never_hides_precision()
    {
        // A non-canonical amount must be visible in output, not silently rounded away.
        Assert.Equal("100.005 DKK", Dkk(100.005m).ToString("G", Invariant));
        Assert.Equal("100.005 DKK", Dkk(100.005m).ToString("R", Invariant));
    }

    [Fact]
    public void Amounts_are_padded_to_the_currency_precision() =>
        Assert.Equal("100.00 DKK", Dkk(100m).ToString("G", Invariant));

    [Fact]
    public void Equal_amounts_always_format_identically()
    {
        // decimal equality ignores scale, so formatting must ignore it too.
        Assert.Equal(Dkk(100m).ToString("G", Invariant), Dkk(100.00m).ToString("G", Invariant));
        Assert.Equal(Dkk(100.5m).ToString("G", Invariant), Dkk(100.500m).ToString("G", Invariant));
    }

    [Fact]
    public void A_digit_count_overrides_the_currency_precision()
    {
        Assert.Equal("$1,235", new Money(1234.56m, Currency.USD).ToString("C0", American));
        Assert.Equal("100.5000 DKK", Dkk(100.5m).ToString("G4", Invariant));
    }

    [Fact]
    public void Negative_amounts_follow_the_cultures_pattern()
    {
        Assert.Equal("-42.50 EUR", new Money(-42.5m, Currency.EUR).ToString("G", Invariant));
        Assert.Equal("-42,50 €", new Money(-42.5m, Currency.EUR).ToString("C", Danish));
    }

    [Fact]
    public void Default_money_formats_as_the_iso_no_currency_code() =>
        Assert.Equal("0 XXX", default(Money).ToString("G", Invariant));

    [Fact]
    public void Unsupported_format_strings_are_rejected()
    {
        Assert.Throws<FormatException>(() => Dkk(1m).ToString("Q", Invariant));
        Assert.Throws<FormatException>(() => Dkk(1m).ToString("G99", Invariant));
    }

    [Fact]
    public void TryFormat_writes_into_a_caller_owned_buffer()
    {
        Span<char> buffer = stackalloc char[32];

        Assert.True(Dkk(100.50m).TryFormat(buffer, out int written, "G", Invariant));
        Assert.Equal("100.50 DKK", new string(buffer[..written]));
    }

    [Fact]
    public void TryFormat_reports_failure_rather_than_truncating()
    {
        Span<char> buffer = stackalloc char[4];

        Assert.False(Dkk(100.50m).TryFormat(buffer, out int written, "G", Invariant));
        Assert.Equal(0, written);
    }

    [Fact]
    public void TryFormat_writes_utf8()
    {
        Span<byte> buffer = stackalloc byte[32];

        Assert.True(Dkk(100.50m).TryFormat(buffer, out int written, "G", Invariant));
        Assert.Equal("100.50 DKK", Encoding.UTF8.GetString(buffer[..written]));
    }

    [Fact]
    public void Utf8_formatting_handles_multi_byte_symbols()
    {
        Span<byte> buffer = stackalloc byte[32];

        Assert.True(new Money(42m, Currency.EUR).TryFormat(buffer, out int written, "C", American));
        Assert.Equal("€42.00", Encoding.UTF8.GetString(buffer[..written]));
    }

    [Fact]
    public void Utf8_formatting_reports_failure_rather_than_truncating()
    {
        Span<byte> buffer = stackalloc byte[3];

        Assert.False(Dkk(100.50m).TryFormat(buffer, out int written, "G", Invariant));
        Assert.Equal(0, written);
    }

    [Fact]
    public void Every_currency_formats_in_every_supported_format()
    {
        foreach (Currency currency in Currency.Known)
        {
            Money value = new(1234.5m, currency);

            foreach (string format in (string[])["G", "R", "C", "I", "N", "L"])
            {
                string text = value.ToString(format, Invariant);
                Assert.False(string.IsNullOrWhiteSpace(text), $"{currency.Code} produced nothing for '{format}'.");
            }
        }
    }

    [Fact]
    public void Interpolation_goes_through_the_span_path()
    {
        // ISpanFormattable means an interpolated string never materialises an intermediate string.
        Money price = Dkk(100.50m);

        Assert.Equal("Total: 100.50 DKK", string.Create(Invariant, $"Total: {price}"));
    }
}
