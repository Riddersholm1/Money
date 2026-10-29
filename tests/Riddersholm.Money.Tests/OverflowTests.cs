using Xunit;

namespace Riddersholm.Money.Tests;

/// <summary>
/// Behaviour at the limits of <see cref="decimal"/>. Overflow was documented but never asserted, so
/// nothing pinned down whether it throws, saturates, or wraps.
/// </summary>
public sealed class OverflowTests
{
    private static Money Max => new(decimal.MaxValue, Currency.DKK);

    private static Money Min => new(decimal.MinValue, Currency.DKK);

    [Fact]
    public void Addition_past_the_maximum_throws_rather_than_wrapping()
    {
        // Saturating or wrapping would invent money out of nothing, which is far worse than throwing.
        Assert.Throws<OverflowException>(() => Max + new Money(1m, Currency.DKK));
        Assert.Throws<OverflowException>(() => Max + Max);
    }

    [Fact]
    public void Subtraction_past_the_minimum_throws()
    {
        Assert.Throws<OverflowException>(() => Min - new Money(1m, Currency.DKK));
        Assert.Throws<OverflowException>(() => Min - Max);
    }

    [Fact]
    public void Multiplication_past_the_maximum_throws() =>
        Assert.Throws<OverflowException>(() => Max * 2m);

    [Fact]
    public void Negation_of_the_extremes_is_well_defined()
    {
        Assert.Equal(Min, -Max);
        Assert.Equal(Max, -Min);
        Assert.Equal(Max, Min.Abs());
    }

    [Fact]
    public void Division_by_a_fraction_can_overflow() =>
        Assert.Throws<OverflowException>(() => Max / 0.5m);

    [Fact]
    public void Division_by_zero_throws_the_right_exception()
    {
        Assert.Throws<DivideByZeroException>(() => new Money(1m, Currency.DKK) / 0m);
        Assert.Throws<DivideByZeroException>(() => new Money(1m, Currency.DKK) / 0);
        Assert.Throws<DivideByZeroException>(() => new Money(1m, Currency.DKK) / Money.Zero(Currency.DKK));
    }

    [Fact]
    public void The_extremes_still_compare_and_format()
    {
        Assert.True(Max > Min);
        Assert.Equal(0, Max.CompareTo(Max));
        Assert.False(string.IsNullOrEmpty(Max.ToString()));
        Assert.False(string.IsNullOrEmpty(Min.ToString("R", System.Globalization.CultureInfo.InvariantCulture)));
    }

    [Fact]
    public void The_extremes_round_trip_through_text()
    {
        System.Globalization.CultureInfo invariant = System.Globalization.CultureInfo.InvariantCulture;

        Assert.Equal(Max, Money.Parse(Max.ToString("R", invariant), invariant));
        Assert.Equal(Min, Money.Parse(Min.ToString("R", invariant), invariant));
    }

    [Fact]
    public void The_extremes_round_trip_through_json()
    {
        Assert.Equal(Max, System.Text.Json.JsonSerializer.Deserialize<Money>(
            System.Text.Json.JsonSerializer.Serialize(Max)));
    }

    [Fact]
    public void Rounding_the_extremes_does_not_throw()
    {
        Assert.Equal(Max, Max.Round());
        Assert.Equal(Min, Min.Round());
        Assert.Equal(Max, Max.Round(0));
    }

    [Fact]
    public void Summing_past_the_maximum_throws_rather_than_silently_wrapping()
    {
        Money[] amounts = [Max, Max];

        Assert.Throws<OverflowException>(() => amounts.Sum());
    }

    [Fact]
    public void Allocating_an_amount_too_large_for_minor_units_is_refused_cleanly()
    {
        // decimal.MaxValue kroner is more øre than a decimal can express, so it cannot be split into
        // payable parts. It must say so rather than produce nonsense.
        Assert.ThrowsAny<Exception>(() => Max.Allocate(3));
    }

    [Fact]
    public void An_exchange_rate_that_overflows_reports_it() =>
        Assert.Throws<OverflowException>(() => new ExchangeRate(Currency.DKK, Currency.EUR, 2m).Convert(Max));
}
