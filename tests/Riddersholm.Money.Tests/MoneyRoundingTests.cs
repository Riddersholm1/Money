using Xunit;

namespace Riddersholm.Money.Tests;

public sealed class MoneyRoundingTests
{
    private static Money Dkk(decimal amount) => new(amount, Currency.DKK);

    [Fact]
    public void Construction_never_rounds()
    {
        // Exact by default: the library does not silently discard precision it was handed.
        Assert.Equal(100.005m, Dkk(100.005m).Amount);
        Assert.False(Dkk(100.005m).IsCanonical);
        Assert.True(Dkk(100.00m).IsCanonical);
    }

    [Fact]
    public void Default_rounding_is_to_even()
    {
        // Matches decimal.Round and is statistically neutral across many roundings.
        Assert.Equal(Dkk(2.22m), Dkk(2.225m).Round());
        Assert.Equal(Dkk(2.24m), Dkk(2.235m).Round());
    }

    [Fact]
    public void Away_from_zero_rounding_is_available_for_tax_regimes_that_require_it()
    {
        Assert.Equal(Dkk(2.23m), Dkk(2.225m).Round(MidpointRounding.AwayFromZero));
        Assert.Equal(Dkk(-2.23m), Dkk(-2.225m).Round(MidpointRounding.AwayFromZero));
    }

    [Theory]
    [InlineData(2.229, MidpointRounding.ToZero, 2.22)]
    [InlineData(-2.229, MidpointRounding.ToZero, -2.22)]
    [InlineData(2.221, MidpointRounding.ToPositiveInfinity, 2.23)]
    [InlineData(2.229, MidpointRounding.ToNegativeInfinity, 2.22)]
    public void Directed_rounding_modes_are_supported(double raw, MidpointRounding mode, double expected) =>
        Assert.Equal(Dkk((decimal)expected), Dkk((decimal)raw).Round(mode));

    [Fact]
    public void Floor_ceiling_and_truncate_are_named_shortcuts()
    {
        Assert.Equal(Dkk(2.22m), Dkk(2.229m).Floor());
        Assert.Equal(Dkk(2.23m), Dkk(2.221m).Ceiling());
        Assert.Equal(Dkk(2.22m), Dkk(2.229m).Truncate());
        Assert.Equal(Dkk(-2.22m), Dkk(-2.229m).Truncate());
    }

    [Fact]
    public void Rounding_uses_the_currency_precision_not_a_fixed_two_decimals()
    {
        Assert.Equal(new Money(1235m, Currency.JPY), new Money(1234.6m, Currency.JPY).Round());
        Assert.Equal(new Money(1.235m, Currency.KWD), new Money(1.2345m, Currency.KWD).Round(MidpointRounding.AwayFromZero));
    }

    [Theory]
    [InlineData("MRU")]
    [InlineData("MGA")]
    public void Rounding_snaps_to_the_increment_not_the_digit_count(string code)
    {
        // The khoum is one fifth of an ouguiya, so valid amounts step by 0.2. Rounding to two
        // decimals would leave 1.37, which cannot be paid.
        Currency currency = Currency.FromCode(code);

        Assert.Equal(new Money(1.4m, currency), new Money(1.37m, currency).Round());
        Assert.Equal(new Money(1.2m, currency), new Money(1.24m, currency).Round());
        Assert.True(new Money(1.4m, currency).IsCanonical);
        Assert.False(new Money(1.37m, currency).IsCanonical);
    }

    [Theory]
    [InlineData("XXX")]
    [InlineData("XTS")]
    public void Currencies_without_a_minor_unit_round_to_a_no_op(string code)
    {
        // You cannot snap to an increment that does not exist.
        Money value = new(1.23456789m, Currency.FromCode(code));

        Assert.Equal(value, value.Round());
        Assert.True(value.IsCanonical);
    }

    [Fact]
    public void Rounding_refuses_to_guess_an_unknown_currency_precision()
    {
        // Reading metadata falls back; *changing money* on a guess does not.
        Money value = new(1.239m, Currency.FromCode("QQQ"));

        Assert.Throws<UnknownCurrencyException>(() => value.Round());
        Assert.Throws<UnknownCurrencyException>(() => value.RoundToCash());
    }

    [Fact]
    public void Rounding_to_an_explicit_precision_works_for_any_currency()
    {
        Money value = new(1.239m, Currency.FromCode("QQQ"));

        Assert.Equal(1.24m, value.Round(2).Amount);
        Assert.Equal(1.2m, value.Round(1).Amount);
        Assert.Equal(1m, value.Round(0).Amount);
    }

    [Fact]
    public void Explicit_precision_is_range_checked()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => Dkk(1m).Round(-1));
        Assert.Throws<ArgumentOutOfRangeException>(() => Dkk(1m).Round(29));
    }

    [Theory]
    [InlineData("CHF", 12.34, 12.35)]  // cash rounds to 0.05
    [InlineData("CHF", 12.32, 12.30)]
    [InlineData("DKK", 12.30, 12.50)]  // cash rounds to 0.50
    [InlineData("DKK", 12.20, 12.00)]
    [InlineData("NOK", 12.40, 12.00)]  // cash rounds to whole kroner
    [InlineData("USD", 12.34, 12.34)]  // no special cash rounding
    public void Cash_rounding_uses_the_smallest_payable_amount(string code, double raw, double expected)
    {
        Currency currency = Currency.FromCode(code);
        Money value = new((decimal)raw, currency);

        Assert.Equal((decimal)expected, value.RoundToCash().Amount);
    }

    [Fact]
    public void Cash_rounding_and_accounting_rounding_can_disagree()
    {
        // The distinction that keeps tills and ledgers reconcilable.
        Money value = new(12.34m, Currency.CHF);

        Assert.Equal(12.34m, value.Round().Amount);
        Assert.Equal(12.35m, value.RoundToCash().Amount);
    }

    [Fact]
    public void Rounding_is_idempotent()
    {
        foreach (Currency currency in Currency.Known)
        {
            Money once = new Money(123.456789m, currency).Round();
            Money twice = once.Round();

            Assert.Equal(once, twice);
            Assert.True(once.IsCanonical, $"{currency.Code} produced a non-canonical amount.");
        }
    }

    [Fact]
    public void Every_currency_can_round_a_representative_amount()
    {
        foreach (Currency currency in Currency.Known)
        {
            Money rounded = new Money(1.23456789m, currency).Round(MidpointRounding.AwayFromZero);

            Assert.True(rounded.IsCanonical);
            Assert.Equal(currency, rounded.Currency);
        }
    }
}
