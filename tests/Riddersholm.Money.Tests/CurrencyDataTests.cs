using Xunit;

namespace Riddersholm.Money.Tests;

/// <summary>
/// Asserts the shape of the generated ISO 4217 table. These are the tests that catch a bad currency
/// data refresh before it reaches anyone's ledger.
/// </summary>
public sealed class CurrencyDataTests
{
    [Fact]
    public void The_expected_number_of_currencies_is_generated() =>
        Assert.Equal(166, Currency.Known.Length);

    [Fact]
    public void Alphabetic_codes_are_unique()
    {
        HashSet<string> codes = [];

        foreach (Currency currency in Currency.Known)
        {
            Assert.True(codes.Add(currency.Code), $"Duplicate alphabetic code '{currency.Code}'.");
        }
    }

    [Fact]
    public void Numeric_codes_are_unique_and_resolvable()
    {
        HashSet<short> numeric = [];

        foreach (Currency currency in Currency.Known)
        {
            Assert.True(numeric.Add(currency.NumericCode), $"Duplicate numeric code on '{currency.Code}'.");

            Assert.True(Currency.TryFromNumericCode(currency.NumericCode, out Currency resolved));
            Assert.Equal(currency, resolved);
        }
    }

    [Fact]
    public void Every_currency_has_usable_metadata()
    {
        foreach (Currency currency in Currency.Known)
        {
            Assert.True(currency.IsKnown);
            Assert.Equal(3, currency.Code.Length);
            Assert.InRange(currency.DecimalDigits, (byte)0, CurrencyInfo.MaximumDecimalDigits);
            Assert.InRange(currency.CashDecimalDigits, (byte)0, CurrencyInfo.MaximumDecimalDigits);
            Assert.True(currency.CashRoundingStep >= 1);
            Assert.False(string.IsNullOrWhiteSpace(currency.EnglishName));
            Assert.False(string.IsNullOrWhiteSpace(currency.Symbol));

            CurrencyInfo info = currency.Info;
            Assert.True(info.IsKnown);
            Assert.Equal(currency, info.Currency);
            Assert.Equal(currency.Code, info.Code);
        }
    }

    [Theory]
    [InlineData("DKK", 208, 2, 100)]
    [InlineData("EUR", 978, 2, 100)]
    [InlineData("USD", 840, 2, 100)]
    [InlineData("JPY", 392, 0, 1)]
    [InlineData("KWD", 414, 3, 1000)]
    [InlineData("CLF", 990, 4, 10000)]
    public void Representative_currencies_carry_the_right_precision(
        string code, short numericCode, byte digits, int minorUnitsPerMajor)
    {
        var currency = Currency.FromCode(code);

        Assert.Equal(numericCode, currency.NumericCode);
        Assert.Equal(digits, currency.DecimalDigits);
        Assert.Equal(minorUnitsPerMajor, currency.MinorUnitsPerMajor);
        Assert.True(currency.HasMinorUnit);
    }

    [Theory]
    [InlineData("MRU")]
    [InlineData("MGA")]
    public void Fifth_minor_units_are_modelled_rather_than_flattened_to_hundredths(string code)
    {
        // ISO records two decimal digits, but the khoum and the iraimbilanja are one fifth of the
        // major unit. Trusting the digit count alone would let 1.37 MRU look like a valid amount.
        var currency = Currency.FromCode(code);

        Assert.Equal(2, currency.DecimalDigits);
        Assert.Equal(5, currency.MinorUnitsPerMajor);
        Assert.Equal(0.2m, currency.MinorUnit);
    }

    [Theory]
    [InlineData("XXX")]
    [InlineData("XTS")]
    public void Codes_without_a_minor_unit_say_so(string code)
    {
        var currency = Currency.FromCode(code);

        Assert.True(currency.IsKnown);
        Assert.False(currency.HasMinorUnit);
        Assert.Equal(0, currency.MinorUnitsPerMajor);
        Assert.Equal(0m, currency.MinorUnit);
    }

    [Theory]
    [InlineData("CHF", 2, 5)]    // 0.05 franc
    [InlineData("DKK", 2, 50)]   // 0.50 krone
    [InlineData("HUF", 0, 5)]    // 5 forint
    [InlineData("NOK", 0, 1)]    // whole kroner
    [InlineData("USD", 2, 1)]    // no special cash rounding
    public void Cash_rounding_is_tracked_separately_from_accounting_precision(
        string code, byte cashDigits, byte cashStep)
    {
        var currency = Currency.FromCode(code);

        Assert.Equal(cashDigits, currency.CashDecimalDigits);
        Assert.Equal(cashStep, currency.CashRoundingStep);
    }

    [Theory]
    [InlineData("XAU")] // gold
    [InlineData("XAG")] // silver
    [InlineData("XPT")] // platinum
    [InlineData("XPD")] // palladium
    [InlineData("XDR")] // special drawing rights
    [InlineData("XSU")] // sucre
    [InlineData("XUA")] // ADB unit of account
    [InlineData("XBA")] // European composite unit
    [InlineData("XAD")] // Arab accounting dinar
    public void Metals_funds_and_units_of_account_are_not_currencies(string code)
    {
        // These parse — any well-formed code does — but they are deliberately not in the table.
        var currency = Currency.FromCode(code);

        Assert.False(currency.IsKnown, $"'{code}' is not money and should not be generated.");
    }

    [Theory]
    [InlineData("XAF")] // CFA franc BEAC
    [InlineData("XOF")] // CFA franc BCEAO
    [InlineData("XPF")] // CFP franc
    [InlineData("XCD")] // East Caribbean dollar
    [InlineData("XCG")] // Caribbean guilder
    public void X_prefixed_codes_that_are_real_money_are_kept(string code) =>
        Assert.True(Currency.FromCode(code).IsKnown, $"'{code}' is circulating money and must be generated.");

    [Theory]
    [InlineData("DEM")] // German mark
    [InlineData("FRF")] // French franc
    [InlineData("ITL")] // Italian lira
    public void Withdrawn_currencies_are_not_generated_but_still_round_trip(string code)
    {
        var currency = Currency.FromCode(code);

        Assert.False(currency.IsKnown);
        Assert.Equal(code, currency.Code);
    }

    [Fact]
    public void Known_currencies_are_ordered_by_code()
    {
        ReadOnlySpan<Currency> known = Currency.Known;

        for (int i = 1; i < known.Length; i++)
        {
            Assert.True(
                string.CompareOrdinal(known[i - 1].Code, known[i].Code) < 0,
                $"'{known[i - 1].Code}' should sort before '{known[i].Code}'.");
        }
    }
}
