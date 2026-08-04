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
        var currency = Currency.FromCode("QLN");

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
        var unknown = Currency.FromCode("ZQZ");

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

        foreach (Currency c1 in known)
        {
            foreach (Currency c2 in known)
            {
                Assert.Equal(
                    Math.Sign(string.CompareOrdinal(c1.Code, c2.Code)),
                    Math.Sign(c1.CompareTo(c2)));
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
    public void N1_a_precision_above_18_rounds_to_the_currencies_real_increment()
    {
        // Pow10Long used to saturate at 10^18, so `unitsPerMajor == Pow10Long(digits)` answered true for
        // every digit count past 18. This currency's increment is 10^-18, but it was being rounded to
        // 20 decimals — that is, not rounded at all.
        Currency currency = RegisterWidePrecisionCurrency();

        Money halfAUnit = new(0.0000000000000000005m, currency);

        Assert.Equal(
            new Money(0.000000000000000001m, currency),
            halfAUnit.Round(MidpointRounding.AwayFromZero));

        Assert.Equal(new Money(0m, currency), halfAUnit.Round(MidpointRounding.ToZero));
    }

    [Fact]
    public void N1_is_canonical_agrees_with_round_above_18_digits()
    {
        // The two share the power-of-ten test, so the saturating version made both wrong together:
        // IsCanonical reported a half-unit amount as representable.
        Currency currency = RegisterWidePrecisionCurrency();

        Assert.False(new Money(0.0000000000000000005m, currency).IsCanonical);
        Assert.True(new Money(0.000000000000000001m, currency).IsCanonical);
        Assert.True(new Money(0.000000000000000001000m, currency).IsCanonical);
    }

    [Theory]
    [InlineData(20, 3L)]    // thirds are not expressible in any number of decimal places
    [InlineData(28, 7L)]
    [InlineData(19, 300L)]  // 3 × 10^2 does not divide 10^19
    public void N1_a_mismatched_divisor_is_rejected_above_18_digits_too(byte digits, long minorUnits)
    {
        // The validation added for M1 computed 10^digits in a long, so it gave up above 18 digits —
        // leaving exactly the registrations that also confused rounding unchecked.
        ArgumentException error = Assert.Throws<ArgumentException>(() =>
            new CurrencyInfo("QNV", 0, "Mismatched", "Q", digits, minorUnits, digits, 1));

        Assert.Equal("minorUnitsPerMajor", error.ParamName);
    }

    [Fact]
    public void N3_allocation_gives_every_recipient_at_most_one_extra_minor_unit()
    {
        // The largest-remainder loop marks each winner as spent, so a surplus at or above the recipient
        // count would land on index 0 repeatedly. The arithmetic says that cannot happen; the guard and
        // this test say so without relying on the argument.
        Money total = new(1_000_000.01m, Currency.DKK);
        int[] weights = [.. Enumerable.Range(1, 97)];

        Money[] parts = total.Allocate(weights);

        Assert.Equal(total, parts.Sum());

        for (int i = 0; i < parts.Length; i++)
        {
            decimal exact = total.Amount * weights[i] / weights.Sum();
            Assert.True(
                Math.Abs(parts[i].Amount - exact) < 0.01m,
                $"Part {i} was {parts[i].Amount}, more than one øre from its exact share {exact}.");
        }
    }

    [Fact]
    public void N4_formatting_is_not_bounded_by_the_pooled_fallback_buffer()
    {
        // The pooled fallback was a fixed 1024 characters, so H1's contract violation survived for any
        // name longer than that. The buffer now doubles until the text fits.
        var currency = Currency.FromCode("QVL");

        if (!currency.IsKnown)
        {
            CurrencyRegistry.Register(new CurrencyInfo(
                code: "QVL",
                numericCode: 0,
                englishName: new string('V', 5_000),
                symbol: "Q",
                decimalDigits: 2,
                minorUnitsPerMajor: 100L,
                cashDecimalDigits: 2,
                cashRoundingStep: 1));
        }

        Money value = new(1234.56m, currency);
        string formatted = value.ToString("L", CultureInfo.InvariantCulture);

        Assert.Equal(new string('V', 5_000), formatted[^5_000..]);

        char[] chars = new char[8192];
        Assert.True(value.TryFormat(chars, out int charsWritten, "L", CultureInfo.InvariantCulture));
        Assert.Equal(formatted, new string(chars, 0, charsWritten));

        byte[] bytes = new byte[8192];
        Assert.True(value.TryFormat(bytes, out int bytesWritten, "L", CultureInfo.InvariantCulture));
        Assert.Equal(formatted, System.Text.Encoding.UTF8.GetString(bytes, 0, bytesWritten));

        // Still honest about a destination that genuinely cannot hold the result.
        Assert.False(value.TryFormat(new byte[64], out _, "L", CultureInfo.InvariantCulture));
    }

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
        return;

        static bool GreaterThan<T>(T left, T right)
            where T : System.Numerics.IComparisonOperators<T, T, bool>
        {
            return left > right;
        }
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

    /// <summary>
    /// A currency whose declared digit count exceeds 18 while its increment does not — a legal
    /// registration, since 10^18 divides 10^20, and the case the saturating power-of-ten test got wrong.
    /// </summary>
    private static Currency RegisterWidePrecisionCurrency()
    {
        var currency = Currency.FromCode("QNP");

        if (!currency.IsKnown)
        {
            CurrencyRegistry.Register(new CurrencyInfo(
                code: "QNP",
                numericCode: 0,
                englishName: "Wide precision test unit",
                symbol: "QNP",
                decimalDigits: 20,
                minorUnitsPerMajor: 1_000_000_000_000_000_000L,
                cashDecimalDigits: 20,
                cashRoundingStep: 1));
        }

        return currency;
    }

    private static Currency RegisterHighPrecisionCurrency()
    {
        var currency = Currency.FromCode("QWE");

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
