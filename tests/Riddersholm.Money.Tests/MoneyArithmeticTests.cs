using Xunit;

namespace Riddersholm.Money.Tests;

public sealed class MoneyArithmeticTests
{
    private static Money Dkk(decimal amount) => new(amount, Currency.DKK);

    private static Money Eur(decimal amount) => new(amount, Currency.EUR);

    [Fact]
    public void Addition_keeps_the_currency()
    {
        Assert.Equal(Dkk(150m), Dkk(100m) + Dkk(50m));
        Assert.Equal(Currency.DKK, (Dkk(100m) + Dkk(50m)).Currency);
    }

    [Fact]
    public void Subtraction_allows_negative_results() =>
        Assert.Equal(Dkk(-20m), Dkk(30m) - Dkk(50m));

    [Fact]
    public void Subtraction_of_equal_amounts_is_zero() =>
        Assert.Equal(Dkk(70m), Dkk(100m) - Dkk(30m));

    [Fact]
    public void Mixing_currencies_is_refused()
    {
        CurrencyMismatchException error = Assert.Throws<CurrencyMismatchException>(() => Dkk(100m) + Eur(50m));

        Assert.Equal(Currency.DKK, error.Left);
        Assert.Equal(Currency.EUR, error.Right);
        Assert.Throws<CurrencyMismatchException>(() => Dkk(100m) - Eur(50m));
    }

    [Fact]
    public void Default_money_is_the_additive_identity()
    {
        // This is what lets Sum() and Aggregate() work without a currency-specific seed.
        Assert.Equal(Dkk(100m), default(Money) + Dkk(100m));
        Assert.Equal(Dkk(100m), Dkk(100m) + default(Money));
        Assert.Equal(Dkk(-100m), default(Money) - Dkk(100m));
        Assert.Equal(Dkk(100m), Dkk(100m) - default(Money));
        Assert.Equal(default(Money), Money.AdditiveIdentity);
    }

    [Fact]
    public void A_non_zero_amount_in_XXX_is_still_a_mismatch()
    {
        // Only the *zero* of Currency.None is the identity. Five of nothing is not five kroner.
        Money fiveOfNothing = new(5m, Currency.XXX);

        Assert.Throws<CurrencyMismatchException>(() => fiveOfNothing + Dkk(100m));
    }

    [Fact]
    public void Multiplication_scales_the_amount()
    {
        Assert.Equal(Dkk(250m), Dkk(100m) * 2.5m);
        Assert.Equal(Dkk(250m), 2.5m * Dkk(100m));
        Assert.Equal(Dkk(300m), Dkk(100m) * 3);
        Assert.Equal(Dkk(300m), 3 * Dkk(100m));
        Assert.Equal(Dkk(300m), Dkk(100m) * 3L);
    }

    [Fact]
    public void Multiplication_does_not_round()
    {
        // The whole point of exact amounts: a chain of operations accumulates no error, and the
        // caller decides where rounding belongs.
        Money result = Dkk(100m) * 1.005m;

        Assert.Equal(100.5m, result.Amount);
        Assert.Equal(Dkk(0.3333m), Dkk(1m) * 0.3333m);
    }

    [Fact]
    public void Division_keeps_full_precision()
    {
        Assert.Equal(Dkk(25m), Dkk(100m) / 4);

        Money third = Dkk(100m) / 3m;

        Assert.NotEqual(33.33m, third.Amount);
        Assert.True(third.Amount > 33.3333m);
    }

    [Fact]
    public void Division_by_zero_throws() =>
        Assert.Throws<DivideByZeroException>(() => Dkk(100m) / 0m);

    [Fact]
    public void Dividing_money_by_money_gives_a_ratio()
    {
        Assert.Equal(4m, Dkk(100m) / Dkk(25m));
        Assert.Throws<CurrencyMismatchException>(() => Dkk(100m) / Eur(25m));
    }

    [Fact]
    public void Negation_flips_the_sign()
    {
        Assert.Equal(Dkk(-100m), -Dkk(100m));
        Assert.Equal(Dkk(100m), -Dkk(-100m));
        Assert.Equal(Dkk(100m), +Dkk(100m));
        Assert.Equal(Dkk(-100m), Dkk(100m).Negate());
    }

    [Fact]
    public void Abs_removes_the_sign()
    {
        Assert.Equal(Dkk(100m), Dkk(-100m).Abs());
        Assert.Equal(Dkk(100m), Dkk(100m).Abs());
    }

    [Fact]
    public void Sign_predicates_describe_the_amount()
    {
        Assert.True(Dkk(0m).IsZero);
        Assert.True(Dkk(1m).IsPositive);
        Assert.True(Dkk(-1m).IsNegative);
        Assert.False(Dkk(0m).IsPositive);
        Assert.False(Dkk(0m).IsNegative);
        Assert.Equal(1, Dkk(5m).Sign);
        Assert.Equal(-1, Dkk(-5m).Sign);
        Assert.Equal(0, Dkk(0m).Sign);
    }

    [Fact]
    public void Zero_is_available_per_currency()
    {
        Assert.Equal(Dkk(0m), Money.Zero(Currency.DKK));
        Assert.True(Money.Zero(Currency.EUR).IsZero);
        Assert.Equal(Currency.EUR, Money.Zero(Currency.EUR).Currency);
    }

    [Fact]
    public void Named_methods_mirror_the_operators()
    {
        Assert.Equal(Dkk(150m), Dkk(100m).Add(Dkk(50m)));
        Assert.Equal(Dkk(50m), Dkk(100m).Subtract(Dkk(50m)));
        Assert.Equal(Dkk(200m), Dkk(100m).Multiply(2m));
        Assert.Equal(Dkk(50m), Dkk(100m).Divide(2m));
        Assert.Equal(2m, Dkk(100m).DivideBy(Dkk(50m)));
    }

    [Fact]
    public void Deconstruction_yields_amount_and_currency()
    {
        (decimal amount, Currency currency) = Dkk(100m);

        Assert.Equal(100m, amount);
        Assert.Equal(Currency.DKK, currency);
    }

    [Fact]
    public void Trailing_zeros_do_not_affect_equality()
    {
        // decimal equality ignores scale, and record struct equality inherits that. Anything else
        // would make 100.0 DKK and 100.00 DKK different amounts of money.
        Assert.Equal(Dkk(100m), Dkk(100.00m));
        Assert.Equal(Dkk(100m).GetHashCode(), Dkk(100.00m).GetHashCode());
    }

    [Fact]
    public void Amounts_in_different_currencies_are_unequal_rather_than_an_error()
    {
        // "Are these the same amount of money?" has a correct answer, and it is no.
        Assert.NotEqual(Dkk(100m), Eur(100m));
        Assert.False(Dkk(100m) == Eur(100m));
        Assert.True(Dkk(100m) != Eur(100m));
    }

    [Fact]
    public void Min_max_and_clamp_require_a_shared_currency()
    {
        Assert.Equal(Dkk(50m), Money.Min(Dkk(50m), Dkk(100m)));
        Assert.Equal(Dkk(100m), Money.Max(Dkk(50m), Dkk(100m)));
        Assert.Equal(Dkk(75m), Money.Clamp(Dkk(200m), Dkk(0m), Dkk(75m)));
        Assert.Equal(Dkk(0m), Money.Clamp(Dkk(-5m), Dkk(0m), Dkk(75m)));

        Assert.Throws<CurrencyMismatchException>(() => Money.Min(Dkk(50m), Eur(100m)));
    }

    [Fact]
    public void WithCurrency_relabels_without_converting()
    {
        Money relabelled = Dkk(100m).WithCurrency(Currency.EUR);

        Assert.Equal(100m, relabelled.Amount);
        Assert.Equal(Currency.EUR, relabelled.Currency);
    }

    [Fact]
    public void Constructing_from_a_code_matches_constructing_from_a_currency() =>
        Assert.Equal(Dkk(100m), new Money(100m, "DKK"));
}
