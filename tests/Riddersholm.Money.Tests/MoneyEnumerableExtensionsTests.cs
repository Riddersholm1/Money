using Xunit;
using static Riddersholm.Money.Tests.TestMoney;

namespace Riddersholm.Money.Tests;

public sealed class MoneyEnumerableExtensionsTests
{
    [Fact]
    public void Sum_adds_a_sequence()
    {
        Money[] amounts = [Dkk(10m), Dkk(20m), Dkk(30m)];

        Assert.Equal(Dkk(60m), amounts.Sum());
    }

    [Fact]
    public void Sum_of_an_empty_sequence_is_the_additive_identity()
    {
        // There is no currency to return zero in, so the identity is the only honest answer.
        Assert.Equal(Money.AdditiveIdentity, Array.Empty<Money>().Sum());
        Assert.True(Array.Empty<Money>().Sum().IsZero);
    }

    [Fact]
    public void Sum_refuses_a_mixed_sequence()
    {
        Money[] mixed = [Dkk(10m), new(20m, Currency.EUR)];

        Assert.Throws<CurrencyMismatchException>(() => mixed.Sum());
    }

    [Fact]
    public void Sum_projects_through_a_selector()
    {
        (string Name, Money Price)[] lines = [("a", Dkk(10m)), ("b", Dkk(15m))];

        Assert.Equal(Dkk(25m), lines.Sum(line => line.Price));
    }

    [Fact]
    public void Average_divides_without_rounding()
    {
        Money[] amounts = [Dkk(10m), Dkk(20m)];

        Assert.Equal(Dkk(15m), amounts.Average());
        Assert.False(new[] { Dkk(10m), Dkk(10m), Dkk(10m), Dkk(10m), Dkk(10m), Dkk(10m), Dkk(1m) }
            .Average().IsCanonical);
    }

    [Fact]
    public void Average_of_an_empty_sequence_throws() =>
        Assert.Throws<InvalidOperationException>(() => Array.Empty<Money>().Average());

    [Fact]
    public void Min_and_max_find_the_extremes()
    {
        Money[] amounts = [Dkk(30m), Dkk(10m), Dkk(20m)];

        Assert.Equal(Dkk(10m), amounts.Min());
        Assert.Equal(Dkk(30m), amounts.Max());
    }

    [Fact]
    public void Min_and_max_refuse_mixed_sequences()
    {
        Money[] mixed = [Dkk(10m), new(20m, Currency.EUR)];

        Assert.Throws<CurrencyMismatchException>(() => mixed.Min());
        Assert.Throws<CurrencyMismatchException>(() => mixed.Max());
    }

    [Fact]
    public void Min_and_max_of_an_empty_sequence_throw()
    {
        Assert.Throws<InvalidOperationException>(() => Array.Empty<Money>().Min());
        Assert.Throws<InvalidOperationException>(() => Array.Empty<Money>().Max());
    }

    [Fact]
    public void Aggregate_works_with_a_default_seed()
    {
        // The reason default(Money) is the additive identity.
        Money[] amounts = [Dkk(10m), Dkk(20m)];

        Assert.Equal(Dkk(30m), amounts.Aggregate(default(Money), (total, next) => total + next));
    }
}
