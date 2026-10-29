using System.Globalization;
using Xunit;

namespace Riddersholm.Money.Tests;

/// <summary>
/// Formatting and parsing across a deliberately awkward spread of cultures. The suite previously
/// covered three Western European ones, which is not enough to trust a globalisation claim.
/// </summary>
public sealed class CultureMatrixTests
{
    /// <summary>
    /// Chosen for the ways they differ: comma decimals, apostrophe and space group separators,
    /// right-to-left scripts, Indic digit grouping, and a culture whose own currency has no minor unit.
    /// </summary>
    public static TheoryData<string> Cultures =>
    [
        "en-US", "da-DK", "de-DE", "fr-FR", "fi-FI", "sv-SE", "de-CH", "it-CH",
        "hi-IN", "ja-JP", "ko-KR", "ar-EG", "he-IL", "fa-IR", "ru-RU", "pl-PL",
        "tr-TR", "el-GR", "pt-BR", "zh-CN", "th-TH", "vi-VN", "cs-CZ", "hu-HU"
    ];

    [Theory]
    [MemberData(nameof(Cultures))]
    public void Round_trip_format_parses_back_under_any_culture(string cultureName)
    {
        // 'R' is invariant by construction, so the caller's culture must not be able to break it.
        CultureInfo culture = new(cultureName);

        foreach (Money value in Representatives())
        {
            string text = value.ToString("R", culture);

            Assert.True(Money.TryParse(text, CultureInfo.InvariantCulture, out Money parsed), $"'{text}' ({cultureName})");
            Assert.Equal(value, parsed);
        }
    }

    [Theory]
    [MemberData(nameof(Cultures))]
    public void General_format_round_trips_within_its_own_culture(string cultureName)
    {
        CultureInfo culture = new(cultureName);

        foreach (Money value in Representatives())
        {
            string text = value.ToString("G", culture);

            Assert.True(Money.TryParse(text, culture, out Money parsed), $"'{text}' ({cultureName})");
            Assert.Equal(value, parsed);
        }
    }

    [Theory]
    [MemberData(nameof(Cultures))]
    public void Currency_format_round_trips_for_the_cultures_own_currency(string cultureName)
    {
        // 'C' is display-oriented and asymmetric on purpose: it writes the *currency's* symbol, while
        // parsing only ever resolves the *culture's* symbol, because "kr" alone is DKK, NOK, SEK and
        // ISK. So it round-trips exactly when the two coincide. For anything else, 'R' is the
        // round-trippable format — see docs/formatting.md.
        CultureInfo culture = new(cultureName);

        if (!TryGetRegionCurrency(culture, out Currency own))
        {
            return;
        }

        foreach (decimal amount in (decimal[])[1234.5m, -1234.5m, 0m])
        {
            Money value = new Money(amount, own).Round();
            string text = value.ToString("C", culture);

            Assert.True(Money.TryParse(text, culture, out Money parsed), $"'{text}' ({cultureName})");
            Assert.Equal(value, parsed);
        }
    }

    [Fact]
    public void Currency_format_is_display_only_for_a_foreign_currency()
    {
        // Pins the asymmetry down rather than leaving it to be rediscovered. en-US has nothing to say
        // about "kr", so its own 'C' output for kroner is not something it can read back.
        CultureInfo american = new("en-US");
        string text = new Money(1234.5m, Currency.DKK).ToString("C", american);

        Assert.Equal("kr1,234.50", text);
        Assert.False(Money.TryParse(text, american, out _));

        // 'R' is what survives.
        Assert.Equal(
            new Money(1234.5m, Currency.DKK),
            Money.Parse(new Money(1234.5m, Currency.DKK).ToString("R", american), CultureInfo.InvariantCulture));
    }

    [Theory]
    [MemberData(nameof(Cultures))]
    public void Every_currency_formats_without_throwing(string cultureName)
    {
        CultureInfo culture = new(cultureName);

        foreach (Currency currency in Currency.Known)
        {
            Money value = new(-1234.5m, currency);

            foreach (string format in (string[])["G", "R", "C", "I", "N", "L"])
            {
                Assert.False(
                    string.IsNullOrWhiteSpace(value.ToString(format, culture)),
                    $"{currency.Code} produced nothing for '{format}' under {cultureName}.");
            }
        }
    }

    [Theory]
    [MemberData(nameof(Cultures))]
    public void A_cultures_own_currency_is_resolvable_from_its_symbol(string cultureName)
    {
        CultureInfo culture = new(cultureName);

        if (!TryGetRegionCurrency(culture, out Currency expected))
        {
            return;
        }

        Money value = new(1234m, expected);
        string text = value.ToString("C", culture);

        Assert.True(Money.TryParse(text, culture, out Money parsed), $"'{text}' ({cultureName})");
        Assert.Equal(expected, parsed.Currency);
    }

    [Fact]
    public void Formatting_does_not_depend_on_the_ambient_culture()
    {
        // A library that reads CultureInfo.CurrentCulture behind the caller's back produces different
        // output on different machines for the same explicit provider.
        CultureInfo original = CultureInfo.CurrentCulture;

        try
        {
            Money value = new(1234.5m, Currency.DKK);

            CultureInfo.CurrentCulture = CultureInfo.InvariantCulture;
            string underInvariant = value.ToString("C", new CultureInfo("da-DK"));

            CultureInfo.CurrentCulture = new CultureInfo("ar-EG");
            Assert.Equal(underInvariant, value.ToString("C", new CultureInfo("da-DK")));
        }
        finally
        {
            CultureInfo.CurrentCulture = original;
        }
    }

    private static IEnumerable<Money> Representatives() =>
    [
        new(1234.5m, Currency.DKK),
        new(-1234.5m, Currency.DKK),
        new(0m, Currency.EUR),
        new(1234m, Currency.JPY),
        new(1.234m, Currency.KWD),
        new(1_000_000.99m, Currency.USD)
    ];

    private static bool TryGetRegionCurrency(CultureInfo culture, out Currency currency)
    {
        currency = Currency.None;

        try
        {
            return Currency.TryFromCode(new RegionInfo(culture.Name).ISOCurrencySymbol, out currency);
        }
        catch (ArgumentException)
        {
            return false;
        }
    }
}
