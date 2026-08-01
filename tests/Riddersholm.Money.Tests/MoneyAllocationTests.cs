using FsCheck.Xunit;
using Xunit;

namespace Riddersholm.Money.Tests;

public sealed class MoneyAllocationTests
{
    private static Money Dkk(decimal amount) => new(amount, Currency.DKK);

    [Fact]
    public void The_canonical_example_distributes_the_stray_ore()
    {
        // 10 / 3 is 3.333…, and three of those rounded come to 9.99 — a øre has evaporated.
        Money[] parts = Dkk(10m).Allocate(3);

        Assert.Equal([Dkk(3.34m), Dkk(3.33m), Dkk(3.33m)], parts);
        Assert.Equal(Dkk(10m), parts.Sum());
    }

    [Fact]
    public void Even_splits_have_no_remainder_to_distribute() =>
        Assert.Equal([Dkk(25m), Dkk(25m), Dkk(25m), Dkk(25m)], Dkk(100m).Allocate(4));

    [Fact]
    public void Negative_amounts_allocate_symmetrically()
    {
        Money[] parts = Dkk(-10m).Allocate(3);

        Assert.Equal([Dkk(-3.34m), Dkk(-3.33m), Dkk(-3.33m)], parts);
        Assert.Equal(Dkk(-10m), parts.Sum());
    }

    [Fact]
    public void Allocating_to_one_recipient_returns_the_whole_amount() =>
        Assert.Equal([Dkk(10m)], Dkk(10m).Allocate(1));

    [Fact]
    public void Allocating_zero_gives_zero_to_everyone() =>
        Assert.Equal([Dkk(0m), Dkk(0m), Dkk(0m)], Dkk(0m).Allocate(3));

    [Fact]
    public void Allocating_less_than_one_unit_per_recipient_still_balances()
    {
        Money[] parts = Dkk(0.02m).Allocate(5);

        Assert.Equal([Dkk(0.01m), Dkk(0.01m), Dkk(0m), Dkk(0m), Dkk(0m)], parts);
        Assert.Equal(Dkk(0.02m), parts.Sum());
    }

    [Fact]
    public void Allocation_can_write_into_a_caller_owned_buffer()
    {
        Span<Money> parts = stackalloc Money[3];

        Dkk(10m).Allocate(parts);

        Assert.Equal(Dkk(3.34m), parts[0]);
        Assert.Equal(Dkk(3.33m), parts[1]);
        Assert.Equal(Dkk(3.33m), parts[2]);
    }

    [Fact]
    public void Ratio_allocation_honours_exact_splits()
    {
        Assert.Equal([Dkk(70m), Dkk(30m)], Dkk(100m).Allocate([70, 30]));
        Assert.Equal([Dkk(50m), Dkk(25m), Dkk(25m)], Dkk(100m).Allocate([2, 1, 1]));
    }

    [Fact]
    public void Ratio_allocation_distributes_the_remainder_by_largest_shortfall()
    {
        Money[] parts = Dkk(0.05m).Allocate([3, 7]);

        Assert.Equal(Dkk(0.05m), parts.Sum());
        Assert.Equal([Dkk(0.02m), Dkk(0.03m)], parts);
    }

    [Fact]
    public void Ratios_need_not_be_normalised()
    {
        // 70:30 and 7:3 describe the same split.
        Assert.Equal(Dkk(100m).Allocate([70, 30]), Dkk(100m).Allocate([7, 3]));
    }

    [Fact]
    public void Decimal_ratios_are_supported()
    {
        Money[] parts = Dkk(100m).Allocate([0.7m, 0.3m]);

        Assert.Equal([Dkk(70m), Dkk(30m)], parts);
    }

    [Fact]
    public void Zero_weights_receive_nothing_but_remain_in_the_result()
    {
        Money[] parts = Dkk(100m).Allocate([1, 0, 1]);

        Assert.Equal([Dkk(50m), Dkk(0m), Dkk(50m)], parts);
    }

    [Fact]
    public void Invalid_ratios_are_rejected()
    {
        Assert.Throws<ArgumentException>(() => Dkk(100m).Allocate(ReadOnlySpan<int>.Empty));
        Assert.Throws<ArgumentException>(() => Dkk(100m).Allocate([1, -1]));
        Assert.Throws<ArgumentException>(() => Dkk(100m).Allocate([0, 0]));
    }

    [Fact]
    public void Invalid_counts_are_rejected()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => Dkk(100m).Allocate(0));
        Assert.Throws<ArgumentOutOfRangeException>(() => Dkk(100m).Allocate(-1));
    }

    [Fact]
    public void Allocating_a_non_canonical_amount_is_refused()
    {
        // No set of payable parts can sum to 10.005 DKK, so the caller must decide how to round.
        InvalidOperationException error =
            Assert.Throws<InvalidOperationException>(() => Dkk(10.005m).Allocate(3));

        Assert.Contains("Round()", error.Message, StringComparison.Ordinal);
        Assert.Equal(Dkk(10m), Dkk(10.005m).Round().Allocate(3).Sum());
    }

    [Fact]
    public void Allocating_a_currency_without_a_minor_unit_is_refused()
    {
        // XXX is a *known* currency with no indivisible unit, so this is not an unknown-currency error.
        InvalidOperationException error =
            Assert.Throws<InvalidOperationException>(() => new Money(10m, Currency.XXX).Allocate(3));

        Assert.IsNotType<UnknownCurrencyException>(error);
    }

    [Fact]
    public void Allocating_an_unknown_currency_is_refused() =>
        Assert.Throws<UnknownCurrencyException>(() => new Money(10m, Currency.FromCode("QQQ")).Allocate(3));

    [Fact]
    public void Allocation_respects_a_three_decimal_currency()
    {
        Money[] parts = new Money(10m, Currency.KWD).Allocate(3);

        Assert.Equal(new Money(10m, Currency.KWD), parts.Sum());
        Assert.Equal(3.334m, parts[0].Amount);
        Assert.Equal(3.333m, parts[1].Amount);
    }

    [Fact]
    public void Allocation_respects_a_fifth_minor_unit()
    {
        // MRU steps by 0.2, so a three-way split of 1 MRU is 0.4 / 0.4 / 0.2, not 0.34 / 0.33 / 0.33.
        Currency mru = Currency.MRU;
        Money[] parts = new Money(1m, mru).Allocate(3);

        Assert.Equal(new Money(1m, mru), parts.Sum());
        Assert.All(parts, part => Assert.True(part.IsCanonical));
        Assert.Equal([new Money(0.4m, mru), new Money(0.4m, mru), new Money(0.2m, mru)], parts);
    }

    [Property(MaxTest = 500)]
    public bool Equal_allocation_never_loses_or_invents_money(int rawUnits, byte rawCount)
    {
        int count = (rawCount % 25) + 1;
        Money total = Dkk(rawUnits / 100m);

        Money[] parts = total.Allocate(count);

        return parts.Sum() == total
            && parts.Length == count
            && Array.TrueForAll(parts, part => part.IsCanonical);
    }

    [Property(MaxTest = 500)]
    public bool Equal_allocation_parts_differ_by_at_most_one_minor_unit(int rawUnits, byte rawCount)
    {
        int count = (rawCount % 25) + 1;
        Money total = Dkk(rawUnits / 100m);

        Money[] parts = total.Allocate(count);
        decimal spread = parts.Max().Amount - parts.Min().Amount;

        return spread <= 0.01m;
    }

    [Property(MaxTest = 500)]
    public bool Ratio_allocation_never_loses_or_invents_money(int rawUnits, byte a, byte b, byte c)
    {
        if (a + b + c == 0)
        {
            return true;
        }

        Money total = Dkk(rawUnits / 100m);
        Money[] parts = total.Allocate([a, b, c]);

        return parts.Sum() == total && Array.TrueForAll(parts, part => part.IsCanonical);
    }

    [Property(MaxTest = 300)]
    public bool Allocation_is_deterministic(int rawUnits, byte rawCount)
    {
        int count = (rawCount % 25) + 1;
        Money total = Dkk(rawUnits / 100m);

        return total.Allocate(count).SequenceEqual(total.Allocate(count));
    }

    [Property(MaxTest = 300)]
    public bool Negating_an_allocation_allocates_the_negation(int rawUnits, byte rawCount)
    {
        int count = (rawCount % 25) + 1;
        Money total = Dkk(rawUnits / 100m);

        Money[] negatedParts = (-total).Allocate(count);
        Money[] partsNegated = [.. total.Allocate(count).Select(part => -part)];

        return negatedParts.SequenceEqual(partsNegated);
    }
}
