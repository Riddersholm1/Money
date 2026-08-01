using Xunit;

namespace Riddersholm.Money.Tests;

/// <summary>
/// The relational operators and <see cref="IComparable{T}"/> deliberately disagree about
/// mixed currencies. These tests pin down both halves.
/// </summary>
public sealed class MoneyComparisonTests
{
    private static Money Dkk(decimal amount) => new(amount, Currency.DKK);

    private static Money Eur(decimal amount) => new(amount, Currency.EUR);

    [Fact]
    public void Operators_compare_amounts_within_a_currency()
    {
        Assert.True(Dkk(100m) > Dkk(50m));
        Assert.True(Dkk(50m) < Dkk(100m));
        Assert.True(Dkk(100m) >= Dkk(100m));
        Assert.True(Dkk(100m) <= Dkk(100m));
    }

    [Fact]
    public void Operators_refuse_to_compare_across_currencies()
    {
        // `if (price > budget)` across two currencies is a bug, and should say so where it happens.
        Assert.Throws<CurrencyMismatchException>(() => Dkk(100m) > Eur(50m));
        Assert.Throws<CurrencyMismatchException>(() => Dkk(100m) < Eur(50m));
        Assert.Throws<CurrencyMismatchException>(() => Dkk(100m) >= Eur(50m));
        Assert.Throws<CurrencyMismatchException>(() => Dkk(100m) <= Eur(50m));
    }

    [Fact]
    public void CompareTo_never_throws_so_that_sorting_works()
    {
        // A comparer that throws does not merely fail — it corrupts the sort, surfacing later as
        // "IComparer.Compare() method returns inconsistent results".
        List<Money> mixed = [Eur(5m), Dkk(100m), Eur(1m), Dkk(2m)];

        mixed.Sort();

        Assert.Equal([Dkk(2m), Dkk(100m), Eur(1m), Eur(5m)], mixed);
    }

    [Fact]
    public void CompareTo_orders_by_currency_before_amount()
    {
        Assert.True(Dkk(1000m).CompareTo(Eur(1m)) < 0);
        Assert.True(Eur(1m).CompareTo(Dkk(1000m)) > 0);
        Assert.Equal(0, Dkk(5m).CompareTo(Dkk(5m)));
    }

    [Fact]
    public void CompareTo_gives_a_consistent_total_order()
    {
        List<Money> values = [Dkk(1m), Dkk(2m), Eur(1m), Eur(2m), new Money(1m, Currency.USD)];

        foreach (Money a in values)
        {
            foreach (Money b in values)
            {
                int forward = a.CompareTo(b);
                int backward = b.CompareTo(a);

                Assert.Equal(Math.Sign(forward), -Math.Sign(backward));
                Assert.Equal(a == b, forward == 0);
            }
        }
    }

    [Fact]
    public void TryCompareTo_reports_rather_than_throws()
    {
        Assert.True(Dkk(100m).TryCompareTo(Dkk(50m), out int result));
        Assert.True(result > 0);

        Assert.False(Dkk(100m).TryCompareTo(Eur(50m), out int mismatch));
        Assert.Equal(0, mismatch);
    }

    [Fact]
    public void Sorting_through_the_non_generic_interface_works()
    {
        IComparable comparable = Dkk(100m);

        Assert.True(comparable.CompareTo(Dkk(50m)) > 0);
        Assert.Equal(1, comparable.CompareTo(null));
        Assert.Throws<ArgumentException>(() => comparable.CompareTo("not money"));
    }

    [Fact]
    public void Money_can_be_used_in_sorted_collections()
    {
        SortedSet<Money> set = [Eur(3m), Dkk(1m), Eur(1m)];

        Assert.Equal([Dkk(1m), Eur(1m), Eur(3m)], set);
    }
}
