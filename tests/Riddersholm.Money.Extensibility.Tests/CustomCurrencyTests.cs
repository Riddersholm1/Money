using Xunit;

namespace Riddersholm.Money.Extensibility.Tests;

/// <summary>
/// End-to-end proof of the extensibility story: this project defines its own currencies in
/// <c>crypto-currencies.json</c> and gets them as first-class members of <see cref="Currency"/>.
/// </summary>
/// <remarks>
/// This is the justification for shipping a Roslyn generator at all. The ISO table alone would not
/// warrant one — it is a static list that a checked-in file could serve. Giving a consumer's own
/// currencies the same treatment is something only a generator can do.
/// </remarks>
public sealed class CustomCurrencyTests
{
    [Fact]
    public void Consumer_defined_currencies_appear_as_members_of_Currency()
    {
        // C# 14 static extension members: this assembly adds members to a type it does not own,
        // so a custom currency reads exactly like an ISO one.
        Currency bitcoin = Currency.XBT;

        Assert.Equal("XBT", bitcoin.Code);
        Assert.Equal(bitcoin, Currency.FromCode("XBT"));
    }

    [Fact]
    public void Registered_metadata_is_available_without_any_startup_call()
    {
        // Registration happens from a [ModuleInitializer]. Nothing in this test set it up.
        Currency bitcoin = Currency.XBT;

        Assert.True(bitcoin.IsKnown);
        Assert.Equal("Bitcoin", bitcoin.EnglishName);
        Assert.Equal("₿", bitcoin.Symbol);
        Assert.Equal(8, bitcoin.DecimalDigits);
        Assert.Equal(100_000_000L, bitcoin.MinorUnitsPerMajor);
    }

    [Fact]
    public void Precisions_beyond_the_iso_maximum_are_supported()
    {
        // ISO 4217 stops at four decimal places, but a satoshi is 1e-8 of a bitcoin and a wei is
        // 1e-18 of an ether. Capping the type at the ISO limit would make those unrepresentable.
        Assert.Equal(0.00000001m, Currency.XBT.MinorUnit);
        Assert.Equal(18, Currency.ETH.DecimalDigits);
        Assert.Equal(1_000_000_000_000_000_000L, Currency.ETH.MinorUnitsPerMajor);
    }

    [Fact]
    public void Omitted_minor_unit_defaults_to_the_usual_power_of_ten() =>
        Assert.Equal(1_000_000_000_000_000_000L, Currency.ETH.MinorUnitsPerMajor);

    [Fact]
    public void Currencies_without_a_fractional_part_are_supported()
    {
        Currency gold = Currency.GLD;

        Assert.Equal(0, gold.DecimalDigits);
        Assert.Equal(1L, gold.MinorUnitsPerMajor);
        Assert.True(gold.HasMinorUnit);
    }

    [Fact]
    public void Custom_currencies_do_not_collide_with_iso_currencies()
    {
        Assert.NotEqual(Currency.XBT, Currency.DKK);
        Assert.NotEqual(Currency.XBT, Currency.ETH);

        // No shared index space is needed: the packed value carries the code itself.
        Assert.Equal(Currency.XBT, Currency.FromCode("xbt"));
    }

    [Fact]
    public void Custom_currencies_are_listed_in_the_registry() =>
        Assert.Contains(CurrencyRegistry.Custom, info => info.Code == "XBT");

    [Fact]
    public void Custom_currencies_are_not_in_the_compile_time_table()
    {
        // Currency.Known is the ISO set; runtime registrations are deliberately kept separate so
        // "which currencies does ISO define?" stays an answerable question.
        foreach (Currency currency in Currency.Known)
        {
            Assert.NotEqual("XBT", currency.Code);
        }
    }

    [Fact]
    public void Iso_currencies_cannot_be_redefined()
    {
        // Silently overriding DKK's precision would corrupt every amount denominated in it.
        ArgumentException error = Assert.Throws<ArgumentException>(() =>
            CurrencyRegistry.Register(new CurrencyInfo("DKK", 208, "Not really", "??", 8, 100_000_000L, 8, 1)));

        Assert.Contains("DKK", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Re_registering_identical_metadata_is_a_no_op()
    {
        // Two assemblies may legitimately declare the same custom currency.
        CurrencyInfo again = new("XBT", 0, "Bitcoin", "₿", 8, 100_000_000L, 8, 1);

        CurrencyRegistry.Register(again);

        Assert.Equal("Bitcoin", Currency.XBT.EnglishName);
    }

    [Fact]
    public void Re_registering_conflicting_metadata_is_rejected()
    {
        CurrencyInfo conflicting = new("XBT", 0, "Bitcoin", "₿", 2, 100L, 2, 1);

        Assert.Throws<ArgumentException>(() => CurrencyRegistry.Register(conflicting));
    }
}
