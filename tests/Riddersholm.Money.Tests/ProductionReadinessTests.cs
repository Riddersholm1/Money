using System.Globalization;
using System.Text.Json;
using Riddersholm.Money.Serialization;
using Xunit;

namespace Riddersholm.Money.Tests;

/// <summary>
/// Regression tests for the production-readiness review. Each one failed before its fix, and each fix
/// replaced a quiet wrong answer with either the right one or a refusal.
/// </summary>
public sealed class ProductionReadinessTests
{
    private static readonly CultureInfo Invariant = CultureInfo.InvariantCulture;

    // ---- P1: JSON read amounts with group separators as the wrong number --------------------------

    [Theory]
    [InlineData("""{"amount":"1,5","currency":"DKK"}""")]       // a European one-and-a-half, read as 15
    [InlineData("""{"amount":"1,000.50","currency":"DKK"}""")]
    [InlineData("""{"amount":" 2 ","currency":"DKK"}""")]
    [InlineData("""{"amount":"1e2","currency":"DKK"}""")]        // decimal turns "1e-30" into 0 unasked
    [InlineData("""{"amount":"(5)","currency":"DKK"}""")]
    public void P1_a_quoted_amount_must_be_a_plain_decimal(string json) =>
        Assert.Throws<JsonException>(() => JsonSerializer.Deserialize<Money>(json));

    [Theory]
    [InlineData("\"1,5 DKK\"")]
    [InlineData("\"1,000.50 DKK\"")]
    [InlineData("\"(5) DKK\"")]
    [InlineData("\"5- DKK\"")]
    [InlineData("\"kr. 5\"")]
    public void P1_the_compact_form_must_be_exactly_what_R_writes(string json) =>
        Assert.Throws<JsonException>(() => JsonSerializer.Deserialize<Money>(json));

    [Theory]
    [InlineData("""{"amount":"-1234.50","currency":"DKK"}""", -1234.50)]
    [InlineData("""{"amount":"0.001","currency":"KWD"}""", 0.001)]
    [InlineData("\"-1234.50 DKK\"", -1234.50)]
    public void P1_well_formed_quoted_amounts_still_read(string json, double expected) =>
        Assert.Equal((decimal)expected, JsonSerializer.Deserialize<Money>(json).Amount);

    [Fact]
    public void P1_a_money_dictionary_key_must_be_in_round_trip_form()
    {
        Assert.Throws<JsonException>(() => JsonSerializer.Deserialize<Dictionary<Money, int>>("""{"1,5 DKK":1}"""));

        Dictionary<Money, int>? read = JsonSerializer.Deserialize<Dictionary<Money, int>>("""{"1.50 DKK":1}""");
        Assert.Equal(1, read![new Money(1.5m, Currency.DKK)]);
    }

    [Fact]
    public void P1_every_currency_still_round_trips_through_every_json_form()
    {
        foreach (MoneyJsonFormat format in Enum.GetValues<MoneyJsonFormat>())
        {
            JsonSerializerOptions options = new JsonSerializerOptions().AddMoney(format);

            foreach (Currency currency in Currency.Known)
            {
                Money value = new(-1234.5678m, currency);
                string json = JsonSerializer.Serialize(value, options);

                Assert.Equal(value, JsonSerializer.Deserialize<Money>(json, options));
            }
        }
    }

    // ---- P2: duplicate JSON properties were resolved by "last one wins" ----------------------------

    [Theory]
    [InlineData("""{"amount":1,"amount":1000000,"currency":"DKK"}""")]
    [InlineData("""{"amount":1,"currency":"DKK","currency":"EUR"}""")]
    [InlineData("""{"amount":1,"Amount":1000000,"currency":"DKK"}""")]
    [InlineData("""{"amount":1,"currency":"DKK","CURRENCY":"EUR"}""")]
    public void P2_a_repeated_property_is_refused_rather_than_resolved(string json)
    {
        // Parsers disagree about which duplicate wins, so the same bytes mean different amounts to
        // different systems. That ambiguity is exactly what a forged payment instruction relies on.
        JsonException error = Assert.Throws<JsonException>(() => JsonSerializer.Deserialize<Money>(json));

        Assert.Contains("more than one", error.Message, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("""{"AMOUNT":1.5,"CURRENCY":"DKK"}""")]
    [InlineData("""{"aMoUnT":1.5,"cUrReNcY":"DKK"}""")]
    [InlineData("""{"\u0061mount":1.5,"curr\u0065ncy":"DKK"}""")]
    public void P2_property_names_really_are_case_insensitive(string json) =>
        Assert.Equal(new Money(1.5m, Currency.DKK), JsonSerializer.Deserialize<Money>(json));

    [Fact]
    public void P2_a_long_escaped_property_name_is_skipped_not_misread()
    {
        string padded = string.Concat(Enumerable.Repeat("\\u0061", 40));
        string json = $$"""{"{{padded}}":7,"amount":1,"currency":"DKK"}""";

        Assert.Equal(new Money(1m, Currency.DKK), JsonSerializer.Deserialize<Money>(json));
    }

    // ---- P3: the format cache was keyed by culture name, so same-named cultures poisoned each other

    [Fact]
    public void P3_a_customised_culture_does_not_inherit_the_real_cultures_cached_format()
    {
        CultureInfo real = CultureInfo.GetCultureInfo("da-DK");
        CultureInfo custom = CustomisedDanish();
        Money amount = new(1234.5m, Currency.DKK);

        string expectedReal = amount.ToString("C", real);

        Assert.Equal(1234.5m.ToString("C", custom), amount.ToString("C", custom));
        Assert.Equal(expectedReal, amount.ToString("C", real));
    }

    [Fact]
    public void P3_the_real_culture_does_not_inherit_a_customised_cultures_cached_format()
    {
        // The other order: whichever culture formatted first used to win, so a customisation made
        // anywhere in the process leaked into every caller of the real culture.
        CultureInfo custom = CustomisedDanish();
        Money amount = new(1234.5m, Currency.EUR);

        _ = amount.ToString("C", custom);

        Assert.DoesNotContain("#", amount.ToString("C", CultureInfo.GetCultureInfo("da-DK")), StringComparison.Ordinal);
        Assert.Contains("#", amount.ToString("C", custom), StringComparison.Ordinal);
    }

    [Fact]
    public void P3_a_writable_culture_changed_between_calls_formats_with_its_current_settings()
    {
        var culture = new CultureInfo("en-US");
        Money amount = new(1234.5m, Currency.USD);

        Assert.Equal("$1,234.50", amount.ToString("C", culture));

        culture.NumberFormat.CurrencyGroupSeparator = "'";

        Assert.Equal("$1'234.50", amount.ToString("C", culture));
    }

    [Fact]
    public void P3_a_bare_number_format_and_its_culture_still_derive_different_symbols()
    {
        // The same read-only NumberFormatInfo backs both calls. Through the culture, da-DK's region owns
        // DKK and its own "kr." is used; bare, there is no region, so the currency's own "kr" is.
        CultureInfo danish = CultureInfo.GetCultureInfo("da-DK");
        Money amount = new(10m, Currency.DKK);

        Assert.Contains("kr.", amount.ToString("C", danish), StringComparison.Ordinal);
        Assert.DoesNotContain("kr.", amount.ToString("C", danish.NumberFormat), StringComparison.Ordinal);
        Assert.Contains("kr.", amount.ToString("C", danish), StringComparison.Ordinal);
    }

    private static CultureInfo CustomisedDanish()
    {
        var custom = (CultureInfo)CultureInfo.GetCultureInfo("da-DK").Clone();
        custom.NumberFormat.CurrencyDecimalSeparator = "#";
        custom.NumberFormat.CurrencyGroupSeparator = "_";
        return custom;
    }

    // ---- P4: the whitespace flags were ignored ---------------------------------------------------

    [Fact]
    public void P4_surrounding_whitespace_is_only_skipped_when_the_style_allows_it()
    {
        Assert.True(Money.TryParse(" 100 DKK ", Money.DefaultStyles, Invariant, out _));
        Assert.False(Money.TryParse(" 100 DKK", Money.DefaultStyles & ~MoneyStyles.AllowLeadingWhite, Invariant, out _));
        Assert.False(Money.TryParse("100 DKK ", Money.DefaultStyles & ~MoneyStyles.AllowTrailingWhite, Invariant, out _));

        // The space between number and code is the separator the formats write, not padding.
        Assert.True(Money.TryParse("100 DKK", MoneyStyles.AllowCurrencyCode, Invariant, out Money parsed));
        Assert.Equal(new Money(100m, Currency.DKK), parsed);
    }

    [Theory]
    [InlineData("-1234.50 DKK", true)]
    [InlineData("1234.50 DKK", true)]
    [InlineData("0 XXX", true)]
    [InlineData("1,234.50 DKK", false)]
    [InlineData("1,5 DKK", false)]
    [InlineData("(5) DKK", false)]
    [InlineData("5- DKK", false)]
    [InlineData(" 5 DKK", false)]
    [InlineData("5", false)]
    public void P4_round_trip_styles_accept_exactly_what_R_writes(string text, bool accepted) =>
        Assert.Equal(accepted, Money.TryParse(text, Money.RoundTripStyles, Invariant, out _));

    [Fact]
    public void P4_every_currency_round_trips_under_the_round_trip_styles()
    {
        foreach (Currency currency in Currency.Known)
        {
            foreach (decimal amount in (decimal[])[0m, -0.5m, 1234567.891m, decimal.MaxValue, decimal.MinValue])
            {
                Money value = new(amount, currency);

                Assert.Equal(value, Money.Parse(value.ToString("R", Invariant), Money.RoundTripStyles, Invariant));
            }
        }
    }

    // ---- P5: fifth-unit and cash rounding rounded twice, the first time in the wrong direction -----

    private const decimal JustUnderFour = 3.9999999999999999999999999999m;

    [Fact]
    public void P5_fifth_unit_rounding_is_exact_at_full_precision()
    {
        // Multiplying by five needs a 30th digit, so decimal rounded the product to 20 khoums before
        // Floor ever saw it — and Floor returned 4.0, above its input.
        Money amount = new(JustUnderFour, Currency.MRU);

        Assert.Equal(3.8m, amount.Floor().Amount);
        Assert.Equal(3.8m, amount.Truncate().Amount);
        Assert.Equal(4.0m, amount.Ceiling().Amount);
        Assert.Equal(4.0m, amount.Round().Amount);
        Assert.False(amount.IsCanonical);
    }

    [Fact]
    public void P5_allocation_refuses_an_amount_that_only_looked_whole_after_rounding()
    {
        // The same rounded product passed allocation's whole-units check, and the parts summed to 4.0
        // rather than to the amount allocated.
        Money amount = new(JustUnderFour, Currency.MRU);

        Assert.Throws<InvalidOperationException>(() => amount.Allocate(2));
        Assert.Throws<InvalidOperationException>(() => amount.Allocate([1, 1]));
    }

    [Fact]
    public void P5_cash_rounding_is_exact_at_full_precision()
    {
        // Dividing by CHF's 0.05 is multiplying by twenty, which needs a 30th digit here too.
        Money swiss = new(JustUnderFour, Currency.CHF);

        Assert.Equal(3.95m, swiss.RoundToCash(MidpointRounding.ToZero).Amount);
        Assert.Equal(3.95m, swiss.RoundToCash(MidpointRounding.ToNegativeInfinity).Amount);
        Assert.Equal(4.00m, swiss.RoundToCash(MidpointRounding.ToPositiveInfinity).Amount);
        Assert.Equal(4.00m, swiss.RoundToCash(MidpointRounding.ToEven).Amount);
        Assert.Equal(-3.95m, (-swiss).RoundToCash(MidpointRounding.ToPositiveInfinity).Amount);
    }

    public static TheoryData<string, MidpointRounding> CashCases()
    {
        TheoryData<string, MidpointRounding> data = [];

        foreach (string code in (string[])["DKK", "CHF", "CAD", "HUF", "MRU", "MGA", "SEK", "EUR"])
        {
            foreach (MidpointRounding mode in Enum.GetValues<MidpointRounding>())
            {
                data.Add(code, mode);
            }
        }

        return data;
    }

    [Theory]
    [MemberData(nameof(CashCases))]
    public void P5_ordinary_amounts_round_exactly_as_the_textbook_formula_says(string code, MidpointRounding mode)
    {
        // The exact arithmetic must change nothing for amounts that never needed it. The reference is
        // the formula the library used before, which is exact whenever the products fit in decimal.
        Currency currency = Currency.FromCode(code);
        decimal increment = CashIncrement(currency);

        for (decimal amount = -30m; amount <= 30m; amount += 0.0125m)
        {
            Money value = new(amount, currency);

            Assert.Equal(Math.Round(amount / increment, 0, mode) * increment, value.RoundToCash(mode).Amount);
            Assert.Equal(
                Math.Round(amount * currency.MinorUnitsPerMajor, 0, mode) / currency.MinorUnitsPerMajor,
                value.Round(mode).Amount);
        }
    }

    private static decimal CashIncrement(Currency currency)
    {
        decimal cash = currency.CashRoundingStep / Pow10(currency.CashDecimalDigits);
        return Math.Max(cash, currency.MinorUnit);
    }

    [Fact]
    public void P5_rounding_never_leaves_a_negative_zero()
    {
        Money tiny = new(-0.01m, Currency.MRU);

        Money rounded = tiny.Round(MidpointRounding.ToZero);

        Assert.Equal(0m, rounded.Amount);
        Assert.False(decimal.IsNegative(rounded.Amount));
    }

    [Fact]
    public void P5_an_invalid_rounding_mode_is_refused_on_every_path()
    {
        const MidpointRounding invalid = (MidpointRounding)42;

        Assert.Throws<ArgumentException>(() => new Money(1.37m, Currency.MRU).Round(invalid));
        Assert.Throws<ArgumentException>(() => new Money(1.4m, Currency.MRU).Round(invalid));
        Assert.Throws<ArgumentException>(() => new Money(1.37m, Currency.DKK).Round(invalid));
        Assert.Throws<ArgumentException>(() => new Money(1.37m, Currency.DKK).RoundToCash(invalid));
    }

    [Fact]
    public void P5_a_registered_currency_finer_than_18_places_rounds_large_amounts_without_overflowing()
    {
        // Rounding scaled by 10^18 by hand, which overflows decimal for anything above about 8×10^10.
        Currency wei = Currency.FromCode("QWF");

        if (!wei.IsKnown)
        {
            CurrencyRegistry.Register(new CurrencyInfo("QWF", 0, "Wide test unit", "QWF", 28, 1_000_000_000_000_000_000L, 28, 1));
        }

        Money large = new(123456789012.123456789012345678m, wei);

        Assert.Equal(123456789012.123456789012345678m, large.Round().Amount);
        Assert.True(large.IsCanonical);
        Assert.Equal(1.123456789012345679m, new Money(1.1234567890123456789m, wei).Round().Amount);
    }

    [Fact]
    public void P5_a_divisor_too_wide_for_128_bit_arithmetic_rounds_exactly_too()
    {
        // 5^14 exceeds 32 bits, which sends rounding down the BigInteger path. The oracle is the
        // definition itself: the nearest whole number of 1/5^14 units, computed with BigInteger.
        const long units = 6_103_515_625L;
        Currency fifths = Currency.FromCode("QFW");

        if (!fifths.IsKnown)
        {
            CurrencyRegistry.Register(new CurrencyInfo("QFW", 0, "Wide fifths", "QFW", 14, units, 14, 1));
        }

        foreach (decimal amount in (decimal[])[0.123456789012345678m, -7.5m / 3m, 1m / 7m, 12345.000000000000001m, -0.00000000000016384m])
        {
            foreach (MidpointRounding mode in Enum.GetValues<MidpointRounding>())
            {
                Money rounded = new Money(amount, fifths).Round(mode);

                Assert.Equal(Oracle(amount, units, mode), rounded.Amount);
                Assert.True(rounded.IsCanonical, $"{rounded} should be a whole number of 1/{units} units.");
            }
        }

        static decimal Oracle(decimal amount, long units, MidpointRounding mode)
        {
            // amount = numerator / 10^scale exactly; scaled = amount × units as an exact fraction.
            int scale = amount.Scale;
            var power = System.Numerics.BigInteger.Pow(10, scale);
            var numerator = System.Numerics.BigInteger.Parse(
                Math.Abs(amount).ToString(Invariant).Replace(".", string.Empty, StringComparison.Ordinal),
                Invariant);

            System.Numerics.BigInteger scaled = numerator * units;
            var whole = System.Numerics.BigInteger.DivRem(scaled, power, out System.Numerics.BigInteger remainder);
            int half = (remainder * 2).CompareTo(power);
            bool negative = amount < 0;

            bool up = remainder.IsZero ? false : mode switch
            {
                MidpointRounding.ToEven => half > 0 || (half == 0 && !whole.IsEven),
                MidpointRounding.AwayFromZero => half >= 0,
                MidpointRounding.ToZero => false,
                MidpointRounding.ToNegativeInfinity => negative,
                _ => !negative
            };

            System.Numerics.BigInteger count = up ? whole + 1 : whole;
            decimal magnitude = (decimal)(count / units) + ((decimal)(count % units) / units);
            return negative ? -magnitude : magnitude;
        }
    }

    // ---- P6: there was no way to exchange amounts as integer minor units -------------------------

    [Theory]
    [InlineData(1050L, "DKK", "10.50")]
    [InlineData(1050L, "JPY", "1050")]
    [InlineData(1050L, "KWD", "1.050")]
    [InlineData(-1L, "EUR", "-0.01")]
    [InlineData(137L, "MRU", "1.37")]   // ISO hundredths, as every payment system counts them
    [InlineData(5L, "XXX", "5")]
    public void P6_amounts_convert_from_minor_units_at_the_iso_exponent(long minorUnits, string code, string expected)
    {
        Money value = Money.FromMinorUnits(minorUnits, Currency.FromCode(code));

        Assert.Equal(decimal.Parse(expected, Invariant), value.Amount);
        Assert.Equal(Currency.FromCode(code), value.Currency);
        Assert.Equal(minorUnits, value.ToMinorUnits());
    }

    [Fact]
    public void P6_from_minor_units_carries_the_currencys_scale()
    {
        Assert.Equal("10.50 DKK", Money.FromMinorUnits(1050, Currency.DKK).ToString("R", Invariant));
        Assert.Equal(2, Money.FromMinorUnits(1000, Currency.DKK).Amount.Scale);
    }

    [Fact]
    public void P6_the_full_range_of_long_converts_both_ways()
    {
        foreach (long minorUnits in (long[])[long.MinValue, long.MinValue + 1, -1, 0, 1, long.MaxValue])
        {
            foreach (Currency currency in Currency.Known)
            {
                Assert.Equal(minorUnits, Money.FromMinorUnits(minorUnits, currency).ToMinorUnits());
            }
        }
    }

    [Fact]
    public void P6_an_amount_finer_than_the_currency_is_refused_not_cut()
    {
        Money tooFine = new(10.505m, Currency.DKK);

        Assert.Throws<InvalidOperationException>(() => tooFine.ToMinorUnits());
        Assert.False(tooFine.TryToMinorUnits(out long units));
        Assert.Equal(0, units);

        Assert.Equal(1051, tooFine.Round(MidpointRounding.AwayFromZero).ToMinorUnits());
    }

    [Fact]
    public void P6_trailing_zeros_beyond_the_currency_are_not_extra_precision()
    {
        Assert.Equal(1050, new Money(10.5000000m, Currency.DKK).ToMinorUnits());
    }

    [Fact]
    public void P6_an_unknown_currency_has_no_exponent_to_scale_by()
    {
        Currency unknown = Currency.FromCode("QQQ");

        Assert.Throws<UnknownCurrencyException>(() => Money.FromMinorUnits(100, unknown));
        Assert.Throws<UnknownCurrencyException>(() => new Money(1m, unknown).ToMinorUnits());
        Assert.False(new Money(1m, unknown).TryToMinorUnits(out _));
    }

    [Fact]
    public void P6_a_result_outside_long_overflows_or_reports_false()
    {
        Money huge = new(100_000_000_000_000_000m, Currency.DKK);

        Assert.Throws<OverflowException>(() => huge.ToMinorUnits());
        Assert.False(huge.TryToMinorUnits(out _));
        Assert.False(new Money(decimal.MaxValue, Currency.KWD).TryToMinorUnits(out _));
    }

    // ---- P7: fallback metadata could be registered as if it were real -----------------------------

    [Fact]
    public void P7_the_fallback_for_an_unknown_code_cannot_be_registered()
    {
        Currency unknown = Currency.FromCode("QFB");
        CurrencyInfo fallback = unknown.Info;

        Assert.False(fallback.IsKnown);
        Assert.Throws<ArgumentException>(() => CurrencyRegistry.Register(fallback));

        // Had it been accepted, IsKnown would have turned true while Info.IsKnown stayed false, and
        // Round() would have trusted the guessed two decimals it exists to refuse.
        Assert.False(unknown.IsKnown);
        Assert.Throws<UnknownCurrencyException>(() => new Money(1.005m, unknown).Round());
    }

    private static decimal Pow10(int exponent)
    {
        decimal result = 1m;

        for (int i = 0; i < exponent; i++)
        {
            result *= 10m;
        }

        return result;
    }
}
