using System.Globalization;
using FsCheck.Xunit;
using Xunit;
using static Riddersholm.Money.Tests.TestMoney;

namespace Riddersholm.Money.Tests;

public sealed class MoneyParsingTests
{
    private static readonly CultureInfo Invariant = CultureInfo.InvariantCulture;
    private static readonly CultureInfo Danish = new("da-DK");
    private static readonly CultureInfo American = new("en-US");
    private static readonly CultureInfo German = new("de-DE");


    [Theory]
    [InlineData("100 DKK", 100)]
    [InlineData("DKK 100", 100)]
    [InlineData("100.50 DKK", 100.50)]
    [InlineData("DKK 100.50", 100.50)]
    [InlineData("100.50DKK", 100.50)]
    [InlineData("DKK100.50", 100.50)]
    [InlineData("  100.50 DKK  ", 100.50)]
    public void Iso_codes_parse_without_a_culture(string text, double expected) =>
        Assert.Equal(Dkk((decimal)expected), Money.Parse(text, Invariant));

    [Theory]
    [InlineData("100,50 kr.", 100.50)]
    [InlineData("kr. 100", 100)]
    [InlineData("kr. 100,50", 100.50)]
    public void Symbols_parse_when_a_culture_identifies_them(string text, double expected) =>
        Assert.Equal(Dkk((decimal)expected), Money.Parse(text, Danish));

    [Fact]
    public void Ambiguous_symbols_are_refused_rather_than_guessed()
    {
        // "kr" is DKK, NOK, SEK and ISK. Without a culture there is no honest answer, so there is no
        // answer at all.
        Assert.False(Money.TryParse("100 kr.", Invariant, out _));
        Assert.False(Money.TryParse("kr. 100", null, out _));
        Assert.False(Money.TryParse("$100", Invariant, out _));
        Assert.False(Money.TryParse("£100", Invariant, out _));
    }

    [Fact]
    public void A_culture_resolves_its_own_symbol_and_only_its_own()
    {
        Assert.Equal(new Money(100m, Currency.USD), Money.Parse("$100", American));
        Assert.Equal(new Money(100m, Currency.EUR), Money.Parse("100,00 €", German));

        // en-US knows '$' means USD; it has nothing to say about 'kr.'
        Assert.False(Money.TryParse("kr. 100", American, out _));
    }

    [Fact]
    public void An_iso_code_wins_over_a_culture_symbol()
    {
        // Explicit beats implicit: the text says DKK, so the answer is DKK even under en-US.
        Assert.Equal(Dkk(100m), Money.Parse("100 DKK", American));
    }

    [Theory]
    [InlineData("-100.50 DKK", -100.50)]
    [InlineData("(100.50) DKK", -100.50)]
    [InlineData("1,234,567.89 DKK", 1234567.89)]
    public void Accounting_forms_are_understood(string text, double expected) =>
        Assert.Equal(Dkk((decimal)expected), Money.Parse(text, Invariant));

    [Fact]
    public void The_cultures_separators_are_respected()
    {
        Assert.Equal(Dkk(1234.5m), Money.Parse("1.234,50 DKK", Danish));
        Assert.Equal(Dkk(1234.5m), Money.Parse("1,234.50 DKK", American));
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("abc")]
    [InlineData("100 kroner")]
    [InlineData("DKK")]
    [InlineData("100 DK")]
    [InlineData("100 DKKK")]
    public void Unrecognisable_text_is_refused(string text) =>
        Assert.False(Money.TryParse(text, Invariant, out _));

    [Fact]
    public void A_bare_number_is_refused_by_default()
    {
        // Money without a currency is almost never what was meant, so RequireCurrency is the default.
        Assert.False(Money.TryParse("100", Invariant, out _));
        Assert.Throws<FormatException>(() => Money.Parse("100", Invariant));
    }

    [Fact]
    public void A_bare_number_is_accepted_when_the_currency_is_not_required()
    {
        const MoneyStyles Styles = Money.DefaultStyles & ~MoneyStyles.RequireCurrency;

        Assert.True(Money.TryParse("100", Styles, Invariant, out Money result));
        Assert.Equal(100m, result.Amount);
        Assert.Equal(Currency.None, result.Currency);
    }

    [Fact]
    public void XXX_is_an_explicit_currency_not_an_absent_one()
    {
        // "1234.5 XXX" names a currency; only text with no currency token at all is refused.
        Assert.True(Money.TryParse("1234.5 XXX", Invariant, out Money result));
        Assert.Equal(Currency.XXX, result.Currency);
        Assert.Equal(1234.5m, result.Amount);
    }

    [Fact]
    public void Styles_can_narrow_what_is_accepted()
    {
        Assert.False(Money.TryParse("(100) DKK", Money.DefaultStyles & ~MoneyStyles.AllowParentheses, Invariant, out _));
        Assert.False(Money.TryParse("1,234 DKK", Money.DefaultStyles & ~MoneyStyles.AllowThousands, Invariant, out _));
        Assert.False(Money.TryParse("$100", Money.DefaultStyles & ~MoneyStyles.AllowCurrencySymbol, American, out _));
        Assert.False(Money.TryParse("100 DKK", Money.DefaultStyles & ~MoneyStyles.AllowCurrencyCode, Invariant, out _));
    }

    [Fact]
    public void Unknown_but_well_formed_codes_parse()
    {
        // Consistent with the rest of the design: an unrecognised currency is still a usable value,
        // which is what lets data with an unfamiliar code survive a round trip through the library.
        Assert.True(Money.TryParse("100 QQQ", Invariant, out Money result));
        Assert.Equal("QQQ", result.Currency.Code);
        Assert.False(result.Currency.IsKnown);
    }

    [Fact]
    public void Null_input_is_refused_rather_than_throwing()
    {
        Assert.False(Money.TryParse((string?)null, Invariant, out _));
        Assert.Throws<ArgumentNullException>(() => Money.Parse((string)null!, Invariant));
    }

    [Fact]
    public void Utf8_input_parses_without_transcoding_first()
    {
        Assert.True(Money.TryParse("100.50 DKK"u8, Invariant, out Money result));
        Assert.Equal(Dkk(100.50m), result);
        Assert.Equal(Dkk(100.50m), Money.Parse("DKK 100.50"u8, Invariant));
    }

    [Fact]
    public void Utf8_input_handles_multi_byte_symbols()
    {
        Assert.True(Money.TryParse("100,00 €"u8, German, out Money result));
        Assert.Equal(new Money(100m, Currency.EUR), result);
    }

    [Fact]
    public void Utf8_input_longer_than_the_stack_buffer_still_parses()
    {
        string padded = new string(' ', 400) + "100.50 DKK";

        Assert.True(Money.TryParse(System.Text.Encoding.UTF8.GetBytes(padded), Invariant, out Money result));
        Assert.Equal(Dkk(100.50m), result);
    }

    [Fact]
    public void Round_trip_format_survives_every_currency()
    {
        foreach (Currency currency in Currency.Known)
        {
            Money original = new(1234.5m, currency);
            string text = original.ToString("R", Invariant);

            Assert.True(Money.TryParse(text, Invariant, out Money parsed), $"Failed to parse '{text}'.");
            Assert.Equal(original, parsed);
        }
    }

    [Fact]
    public void General_format_round_trips_under_the_invariant_culture()
    {
        foreach (Currency currency in Currency.Known)
        {
            Money original = new(-98765.4321m, currency);

            Assert.Equal(original, Money.Parse(original.ToString("G", Invariant), Invariant));
            Assert.Equal(original, Money.Parse(original.ToString("I", Invariant), Invariant));
        }
    }

    [Fact]
    public void Currency_format_round_trips_within_its_own_culture()
    {
        Money original = Dkk(1234.56m);

        Assert.Equal(original, Money.Parse(original.ToString("C", Danish), Danish));
        Assert.Equal(original, Money.Parse(original.Negate().ToString("C", Danish), Danish).Negate());
    }

    [Property(MaxTest = 500)]
    public bool Round_trip_format_always_parses_back(int rawUnits, byte currencyIndex)
    {
        Currency currency = Currency.Known[currencyIndex % Currency.Known.Length];
        Money original = new(rawUnits / 100m, currency);

        return Money.TryParse(original.ToString("R", Invariant), Invariant, out Money parsed)
            && parsed == original;
    }

    [Property(MaxTest = 300)]
    public bool Utf8_and_utf16_parsing_agree(int rawUnits)
    {
        Money original = Dkk(rawUnits / 100m);
        string text = original.ToString("R", Invariant);

        return Money.TryParse(text, Invariant, out Money fromChars)
            && Money.TryParse(System.Text.Encoding.UTF8.GetBytes(text), Invariant, out Money fromBytes)
            && fromChars == fromBytes;
    }
}
