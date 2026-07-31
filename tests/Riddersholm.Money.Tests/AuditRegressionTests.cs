using System.Globalization;
using System.Runtime.CompilerServices;
using Xunit;

namespace Riddersholm.Money.Tests;

/// <summary>
/// Regression tests for defects found in the production-readiness audit. Each one failed before the
/// corresponding fix.
/// </summary>
public sealed class AuditRegressionTests
{
    /// <summary>A registered currency with a deliberately long name, to push formatting past any internal buffer.</summary>
    private static Currency LongNamedCurrency()
    {
        Currency currency = Currency.FromCode("QLN");

        if (!currency.IsKnown)
        {
            CurrencyRegistry.Register(new CurrencyInfo(
                code: "QLN",
                numericCode: 0,
                englishName: new string('N', 200),
                symbol: "Q",
                decimalDigits: 2,
                minorUnitsPerMajor: 100L,
                cashDecimalDigits: 2,
                cashRoundingStep: 1));
        }

        return currency;
    }

    [Fact]
    public void H1_utf8_formatting_is_bounded_by_the_callers_buffer_not_an_internal_one()
    {
        // IUtf8SpanFormattable says false means "the destination was too small". Formatting through a
        // fixed internal buffer made that a lie: a 4 KB destination failed for a 200-character name.
        Money value = new(1234.56m, LongNamedCurrency());
        byte[] roomy = new byte[4096];

        Assert.True(value.TryFormat(roomy, out int written, "L", CultureInfo.InvariantCulture));
        Assert.Equal(value.ToString("L", CultureInfo.InvariantCulture), System.Text.Encoding.UTF8.GetString(roomy, 0, written));
    }

    [Fact]
    public void H1_utf8_formatting_still_reports_a_genuinely_small_buffer()
    {
        Span<byte> tiny = stackalloc byte[4];

        Assert.False(new Money(1234.56m, Currency.DKK).TryFormat(tiny, out int written, "G", CultureInfo.InvariantCulture));
        Assert.Equal(0, written);
    }

    [Fact]
    public void H1_char_formatting_handles_names_longer_than_the_stack_buffer()
    {
        Money value = new(1234.56m, LongNamedCurrency());
        char[] roomy = new char[4096];

        Assert.True(value.TryFormat(roomy, out int written, "L", CultureInfo.InvariantCulture));
        Assert.Contains(new string('N', 200), new string(roomy, 0, written), StringComparison.Ordinal);
    }

    [Fact]
    public void H2_is_canonical_never_throws_however_extreme_the_amount()
    {
        // A property that throws OverflowException is a debugger hazard and a guideline violation.
        Currency wei = RegisterHighPrecisionCurrency();

        _ = new Money(decimal.MaxValue, wei).IsCanonical;
        _ = new Money(decimal.MinValue, wei).IsCanonical;
        _ = new Money(decimal.MaxValue, Currency.DKK).IsCanonical;
        _ = new Money(decimal.MaxValue, Currency.MRU).IsCanonical;
    }

    [Fact]
    public void H2_is_canonical_still_answers_correctly_for_ordinary_amounts()
    {
        Assert.True(new Money(100.00m, Currency.DKK).IsCanonical);
        Assert.False(new Money(100.005m, Currency.DKK).IsCanonical);
        Assert.True(new Money(1.4m, Currency.MRU).IsCanonical);
        Assert.False(new Money(1.37m, Currency.MRU).IsCanonical);
        Assert.True(new Money(1.23456789m, Currency.XXX).IsCanonical);
    }

    [Fact]
    public void H3_ratio_allocation_survives_weights_spanning_many_orders_of_magnitude()
    {
        // Passing raw amounts as weights is entirely natural, and used to overflow because the
        // implementation multiplied by the weight before dividing by the total: 10^11 minor units
        // times a 10^18 weight is 10^29, past decimal's 7.9×10^28 ceiling.
        Money total = new(1_000_000_000m, Currency.DKK);
        decimal[] weights = [1m, 1_000_000_000_000_000_000m, 12345.6789m];

        Money[] parts = total.Allocate(weights);

        Assert.Equal(total, parts.Sum());
        Assert.All(parts, part => Assert.True(part.IsCanonical));
    }

    [Fact]
    public void H3_ratio_allocation_survives_a_very_large_total()
    {
        Money total = new(792_281_625_142_643m, Currency.DKK);

        Money[] parts = total.Allocate([1, 1, 1]);

        Assert.Equal(total, parts.Sum());
    }

    [Fact]
    public void H4_allocating_a_currency_with_no_minor_unit_is_not_an_unknown_currency_error()
    {
        // XXX and XTS are known currencies. They simply cannot be subdivided.
        foreach (Currency currency in (Currency[])[Currency.XXX, Currency.XTS])
        {
            Assert.True(currency.IsKnown);

            InvalidOperationException error =
                Assert.Throws<InvalidOperationException>(() => new Money(10m, currency).Allocate(3));

            Assert.IsNotType<UnknownCurrencyException>(error);
            Assert.Contains(currency.Code, error.Message, StringComparison.Ordinal);
        }
    }

    [Fact]
    public void H4_allocating_a_genuinely_unknown_currency_still_reports_one()
    {
        UnknownCurrencyException error =
            Assert.Throws<UnknownCurrencyException>(() => new Money(10m, Currency.FromCode("ZQX")).Allocate(3));

        Assert.Equal(Currency.FromCode("ZQX"), error.Currency);
    }

    [Fact]
    public void H4_every_unknown_currency_exception_carries_its_currency()
    {
        // The message-only constructor used to leave Currency as default, so the diagnostic data lied.
        Money value = new(1.239m, Currency.FromCode("ZQY"));

        UnknownCurrencyException fromRound = Assert.Throws<UnknownCurrencyException>(() => value.Round());
        UnknownCurrencyException fromCash = Assert.Throws<UnknownCurrencyException>(() => value.RoundToCash());

        Assert.Equal(value.Currency, fromRound.Currency);
        Assert.Equal(value.Currency, fromCash.Currency);
    }

    [Fact]
    public void H5_comparing_currencies_allocates_nothing()
    {
        Currency known = Currency.DKK;
        Currency unknown = Currency.FromCode("ZQZ");

        // Warm up anything lazily initialised so the measurement sees only the comparison.
        _ = known.CompareTo(unknown);

        long before = GC.GetAllocatedBytesForCurrentThread();

        for (int i = 0; i < 1_000; i++)
        {
            _ = known.CompareTo(unknown);
            _ = unknown.CompareTo(known);
            _ = unknown.CompareTo(unknown);
        }

        Assert.Equal(0, GC.GetAllocatedBytesForCurrentThread() - before);
    }

    [Fact]
    public void H5_comparing_money_allocates_nothing()
    {
        Money left = new(1m, Currency.FromCode("ZQA"));
        Money right = new(2m, Currency.FromCode("ZQB"));

        _ = left.CompareTo(right);

        long before = GC.GetAllocatedBytesForCurrentThread();

        for (int i = 0; i < 1_000; i++)
        {
            _ = left.CompareTo(right);
        }

        Assert.Equal(0, GC.GetAllocatedBytesForCurrentThread() - before);
    }

    [Fact]
    public void H5_currency_ordering_is_still_alphabetical()
    {
        List<Currency> currencies = [Currency.USD, Currency.FromCode("AAA"), Currency.DKK, Currency.FromCode("ZZZ")];
        currencies.Sort();

        Assert.Equal(
            ["AAA", "DKK", "USD", "ZZZ"],
            currencies.Select(c => c.Code));
    }

    [Fact]
    public void H5_currency_ordering_matches_ordinal_string_ordering_across_the_whole_table()
    {
        ReadOnlySpan<Currency> known = Currency.Known;

        for (int i = 0; i < known.Length; i++)
        {
            for (int j = 0; j < known.Length; j++)
            {
                Assert.Equal(
                    Math.Sign(string.CompareOrdinal(known[i].Code, known[j].Code)),
                    Math.Sign(known[i].CompareTo(known[j])));
            }
        }
    }

    [Theory]
    [InlineData(2, 1_000_000_000_000_000_000L)] // 18-decimal divisor claimed at 2 digits
    [InlineData(2, 3L)]                         // thirds are not expressible in decimal places
    [InlineData(0, 100L)]                       // hundredths claimed at zero digits
    public void M1_a_divisor_that_the_digit_count_cannot_express_is_rejected(byte digits, long minorUnits)
    {
        // Accepting these silently produces amounts that report IsCanonical while rounding to an
        // increment the currency cannot represent.
        ArgumentException error = Assert.Throws<ArgumentException>(() =>
            new CurrencyInfo("QMM", 0, "Mismatched", "Q", digits, minorUnits, digits, 1));

        Assert.Equal("minorUnitsPerMajor", error.ParamName);
    }

    [Theory]
    [InlineData(2, 100L)]   // the ordinary case
    [InlineData(2, 5L)]     // MRU and MGA: a fifth of the major unit
    [InlineData(2, 20L)]    // a twentieth is expressible in two places
    [InlineData(0, 1L)]     // JPY
    [InlineData(3, 1000L)]  // KWD
    [InlineData(8, 100_000_000L)]
    [InlineData(2, 0L)]     // no minor unit at all
    public void M1_a_divisor_the_digit_count_can_express_is_accepted(byte digits, long minorUnits) =>
        Assert.Equal(minorUnits, new CurrencyInfo("QMM", 0, "Fine", "Q", digits, minorUnits, digits, 1).MinorUnitsPerMajor);

    [Fact]
    public void M2_the_code_validation_message_matches_the_behaviour()
    {
        // Lower case is accepted and normalised, so claiming "uppercase" was misleading.
        Assert.Equal("DKK", new CurrencyInfo("dkk", 208, "Danish Krone", "kr", 2, 100L, 2, 1).Code);

        ArgumentException error = Assert.Throws<ArgumentException>(() =>
            new CurrencyInfo("D1", 0, "Bad", "B", 2, 100L, 2, 1));

        Assert.DoesNotContain("uppercase", error.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void M3_currency_satisfies_the_generic_comparison_constraint()
    {
        // Currency defined all four relational operators without declaring the interface, so
        // generic-math code could not see them.
        Assert.True(GreaterThan(Currency.USD, Currency.DKK));
        Assert.False(GreaterThan(Currency.DKK, Currency.USD));

        static bool GreaterThan<T>(T left, T right)
            where T : System.Numerics.IComparisonOperators<T, T, bool> => left > right;
    }

    [Fact]
    public void Struct_sizes_match_what_the_architecture_documentation_claims()
    {
        // docs/architecture.md commits to these. A field added without thought would silently regress
        // the whole point of the design.
        Assert.Equal(4, Unsafe.SizeOf<Currency>());
        Assert.Equal(24, Unsafe.SizeOf<Money>());
        Assert.Equal(24, Unsafe.SizeOf<ExchangeRate>());
    }

    private static Currency RegisterHighPrecisionCurrency()
    {
        Currency currency = Currency.FromCode("QWE");

        if (!currency.IsKnown)
        {
            CurrencyRegistry.Register(new CurrencyInfo(
                code: "QWE",
                numericCode: 0,
                englishName: "High precision test unit",
                symbol: "QWE",
                decimalDigits: 18,
                minorUnitsPerMajor: 1_000_000_000_000_000_000L,
                cashDecimalDigits: 18,
                cashRoundingStep: 1));
        }

        return currency;
    }
}
