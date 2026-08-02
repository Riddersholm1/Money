using System.Globalization;
using Xunit;

namespace Riddersholm.Money.Tests;

/// <summary>
/// The invariants this library promises, asserted across <em>every</em> currency rather than a chosen
/// handful.
/// </summary>
/// <remarks>
/// <para>
/// Three of the serious defects found in review shared one shape: a guarantee written in a doc comment
/// and never asserted anywhere. Reading more carefully does not close that gap; executing the
/// guarantees does. Each test here takes a property the documentation states unconditionally and runs
/// it over the full cross product of the 166 generated currencies and a set of awkward amounts.
/// </para>
/// <para>
/// The cash-rounding test is why this file exists. Cash precision is documented as being coarser than
/// accounting precision, never finer, and for MRU it was finer — so
/// <c>new Money(1.37m, Currency.MRU).RoundToCash()</c> returned an amount nobody can pay. Four
/// hand-picked currencies were tested; the one that mattered was not among them.
/// </para>
/// </remarks>
public sealed class BankingInvariantTests
{
    /// <summary>
    /// Amounts chosen to sit on the boundaries rounding actually cares about: exact minor units, exact
    /// halves of one, values below the smallest unit, and values carrying more precision than any
    /// currency has.
    /// </summary>
    /// <remarks>
    /// Deliberately excludes <see cref="decimal.MaxValue"/>. Rounding to a fifth-unit currency scales
    /// by the divisor, so the extremes overflow for reasons that have nothing to do with the invariants
    /// under test; <see cref="Rounding_overflows_rather_than_returning_a_wrong_answer"/> covers that
    /// boundary separately and on purpose.
    /// </remarks>
    public static readonly decimal[] Amounts =
    [
        0m,
        0.001m, -0.001m,
        0.005m, -0.005m,
        0.01m, -0.01m,
        0.1m, -0.1m,
        0.2m, -0.2m,
        0.5m, -0.5m,
        1m, -1m,
        1.37m, -1.37m,
        2.5m, -2.5m,
        12.345m, -12.345m,
        99.995m, -99.995m,
        1234.5678m, -1234.5678m,
        123456789.123456789m, -123456789.123456789m,
        0.1234567890123456789012345678m,
    ];

    public static readonly MidpointRounding[] Modes =
    [
        MidpointRounding.ToEven,
        MidpointRounding.AwayFromZero,
        MidpointRounding.ToZero,
        MidpointRounding.ToNegativeInfinity,
        MidpointRounding.ToPositiveInfinity,
    ];

    /// <summary>
    /// Every currency's declared cash increment must be a whole number of its minor units.
    /// </summary>
    /// <remarks>
    /// This is the invariant that failed. MRU declares two cash decimals — an increment of 0.01 — while
    /// its khoum is one <em>fifth</em> of an ouguiya, so 0.01 MRU is not an amount that exists. The
    /// check is on the data rather than on behaviour, so a bad currency refresh fails here with a clear
    /// message instead of silently producing unpayable amounts later.
    /// </remarks>
    [Fact]
    public void The_cash_increment_is_a_whole_number_of_minor_units()
    {
        foreach (Currency currency in Currency.Known)
        {
            long unitsPerMajor = currency.MinorUnitsPerMajor;

            if (unitsPerMajor == 0)
            {
                continue;
            }

            decimal cashUnits = (decimal)currency.CashRoundingStep * unitsPerMajor / Pow10(currency.CashDecimalDigits);

            Assert.True(
                cashUnits >= 1m && decimal.Truncate(cashUnits) == cashUnits,
                $"{currency.Code} declares a cash increment of {currency.CashRoundingStep}e-{currency.CashDecimalDigits}, "
              + $"which is {cashUnits} minor units. Cash cannot be finer than the currency's own unit "
              + $"(1/{unitsPerMajor}), so RoundToCash would return an amount nobody can pay.");
        }
    }

    [Fact]
    public void Cash_rounding_always_produces_a_payable_amount()
    {
        foreach (Currency currency in Currency.Known)
        {
            foreach (decimal amount in Amounts)
            {
                foreach (MidpointRounding mode in Modes)
                {
                    Money rounded = new Money(amount, currency).RoundToCash(mode);

                    Assert.True(
                        rounded.IsCanonical,
                        $"{amount} {currency.Code} rounded to cash ({mode}) gave {rounded.Amount}, which is "
                      + $"not a whole number of {currency.Code} minor units and therefore cannot be paid.");
                }
            }
        }
    }

    [Fact]
    public void Accounting_rounding_always_produces_a_canonical_amount()
    {
        foreach (Currency currency in Currency.Known)
        {
            foreach (decimal amount in Amounts)
            {
                foreach (MidpointRounding mode in Modes)
                {
                    Money value = new(amount, currency);

                    Assert.True(value.Round(mode).IsCanonical, $"Round({mode}) on {amount} {currency.Code}");
                }

                Money original = new(amount, currency);

                Assert.True(original.Floor().IsCanonical, $"Floor on {amount} {currency.Code}");
                Assert.True(original.Ceiling().IsCanonical, $"Ceiling on {amount} {currency.Code}");
                Assert.True(original.Truncate().IsCanonical, $"Truncate on {amount} {currency.Code}");
            }
        }
    }

    /// <summary>
    /// Cash rounding may be coarser than accounting rounding, never finer.
    /// </summary>
    /// <remarks>
    /// Stated as: a cash-rounded amount is unchanged by a subsequent accounting round. If cash were the
    /// finer of the two, accounting rounding would have something left to remove — which is exactly
    /// what MRU did.
    /// </remarks>
    [Fact]
    public void Cash_rounding_is_never_finer_than_accounting_rounding()
    {
        foreach (Currency currency in Currency.Known)
        {
            foreach (decimal amount in Amounts)
            {
                Money cash = new Money(amount, currency).RoundToCash();

                Assert.Equal(cash, cash.Round());
            }
        }
    }

    [Fact]
    public void Rounding_is_idempotent_in_every_mode()
    {
        foreach (Currency currency in Currency.Known)
        {
            foreach (decimal amount in Amounts)
            {
                foreach (MidpointRounding mode in Modes)
                {
                    Money once = new Money(amount, currency).Round(mode);
                    Money cashOnce = new Money(amount, currency).RoundToCash(mode);

                    Assert.Equal(once, once.Round(mode));
                    Assert.Equal(cashOnce, cashOnce.RoundToCash(mode));
                }
            }
        }
    }

    /// <summary>Rounding never moves an amount by more than one increment, nor across it in the wrong direction.</summary>
    [Fact]
    public void Rounding_never_moves_an_amount_further_than_one_increment()
    {
        foreach (Currency currency in Currency.Known)
        {
            decimal increment = currency.MinorUnit;

            if (increment == 0m)
            {
                continue;
            }

            foreach (decimal amount in Amounts)
            {
                Money value = new(amount, currency);

                Assert.True(
                    Math.Abs(value.Round().Amount - amount) <= increment,
                    $"Round moved {amount} {currency.Code} by more than {increment}.");

                Assert.True(value.Floor().Amount <= amount, $"Floor raised {amount} {currency.Code}.");
                Assert.True(value.Ceiling().Amount >= amount, $"Ceiling lowered {amount} {currency.Code}.");
                Assert.True(
                    Math.Abs(value.Truncate().Amount) <= Math.Abs(amount),
                    $"Truncate moved {amount} {currency.Code} away from zero.");
            }
        }
    }

    [Fact]
    public void Allocation_preserves_the_total_and_spreads_within_one_minor_unit()
    {
        foreach (Currency currency in Currency.Known)
        {
            if (!currency.HasMinorUnit)
            {
                // XXX and XTS have no indivisible unit, so there is nothing to allocate in.
                continue;
            }

            decimal increment = currency.MinorUnit;

            foreach (decimal amount in Amounts)
            {
                // Allocation refuses non-canonical amounts by design, so start from a payable one.
                Money total = new Money(amount, currency).Round();

                foreach (int count in (int[])[1, 2, 3, 7, 12, 97])
                {
                    Money[] parts = total.Allocate(count);

                    Assert.Equal(total, parts.Sum());
                    Assert.All(parts, part => Assert.True(part.IsCanonical));

                    decimal spread = parts.Max(p => p.Amount) - parts.Min(p => p.Amount);

                    Assert.True(
                        spread <= increment,
                        $"Splitting {total} {count} ways spread the parts by {spread}, more than one "
                      + $"minor unit ({increment}).");
                }
            }
        }
    }

    [Fact]
    public void Allocation_by_ratio_preserves_the_total()
    {
        int[][] ratioSets = [[1], [1, 1], [70, 30], [1, 2, 3], [1, 1, 1, 1, 1, 1, 1], [5, 0, 5]];

        foreach (Currency currency in Currency.Known)
        {
            if (!currency.HasMinorUnit)
            {
                continue;
            }

            foreach (decimal amount in Amounts)
            {
                Money total = new Money(amount, currency).Round();

                foreach (int[] ratios in ratioSets)
                {
                    Money[] parts = total.Allocate(ratios);

                    Assert.Equal(total, parts.Sum());
                    Assert.All(parts, part => Assert.True(part.IsCanonical));
                }
            }
        }
    }

    [Fact]
    public void The_round_trip_format_reads_back_exactly()
    {
        foreach (Currency currency in Currency.Known)
        {
            foreach (decimal amount in Amounts)
            {
                Money original = new(amount, currency);
                string text = original.ToString("R", CultureInfo.InvariantCulture);
                Money parsed = Money.Parse(text, CultureInfo.InvariantCulture);

                Assert.Equal(original, parsed);
                Assert.Equal(original.Currency.Code, parsed.Currency.Code);
                // Equality ignores trailing zeros, so pin the exact value as well.
                Assert.Equal(original.Amount, parsed.Amount);
            }
        }
    }

    [Fact]
    public void Json_round_trips_every_currency_exactly()
    {
        foreach (Currency currency in Currency.Known)
        {
            foreach (decimal amount in Amounts)
            {
                Money original = new(amount, currency);
                string json = System.Text.Json.JsonSerializer.Serialize(original);
                Money parsed = System.Text.Json.JsonSerializer.Deserialize<Money>(json);

                Assert.Equal(original.Amount, parsed.Amount);
                Assert.Equal(original.Currency, parsed.Currency);
            }
        }
    }

    /// <summary>
    /// Arithmetic that stays inside <see cref="decimal"/>'s significand is exact, for every currency.
    /// </summary>
    /// <remarks>
    /// The amounts used here are the realistic ones — four decimal places or fewer, which covers every
    /// ISO currency. The boundary beyond that is stated separately and deliberately by
    /// <see cref="Precision_is_lost_only_when_a_result_needs_more_digits_than_decimal_holds"/>.
    /// </remarks>
    [Fact]
    public void Arithmetic_is_exact_across_every_currency()
    {
        decimal[] realistic = [0m, 0.01m, -0.01m, 1m, -1m, 12.345m, -12.345m, 1234.5678m, -1234.5678m, 123456789.123456789m];

        foreach (Currency currency in Currency.Known)
        {
            foreach (decimal amount in realistic)
            {
                Money value = new(amount, currency);
                Money other = new(12.345m, currency);

                Assert.Equal(value, value + other - other);
                Assert.Equal(value, -(-value));
                Assert.Equal(value, value + Money.Zero(currency));
                Assert.Equal(Money.Zero(currency), value - value);
                Assert.Equal(value, value * 2m - value);

                foreach (decimal factor in (decimal[])[2m, 3m, 7m, 100m])
                {
                    Assert.Equal(value, value * factor / factor);
                }
            }
        }
    }

    /// <summary>
    /// Precision is lost exactly when a result needs more significant digits than a
    /// <see cref="decimal"/> has, and not otherwise.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The library says arithmetic is "exact", and that claim needs its boundary stated rather than
    /// left for someone to discover during a reconciliation. Exact here means <em>nothing is rounded to
    /// the currency's precision behind your back</em> — it does not mean infinite-precision rational
    /// arithmetic, because the underlying type carries 28-29 significant digits and no more.
    /// </para>
    /// <para>
    /// The operation that crosses the line is not the obvious one. Adding a small full-precision amount
    /// to a larger one needs digits on both sides of the point and silently rescales, while multiplying
    /// the same amount by three and dividing back is exact. So the rule is about the <em>width of the
    /// result</em>, not about which operator was used — and any amount at a real currency's precision
    /// is nowhere near the limit.
    /// </para>
    /// </remarks>
    [Fact]
    public void Precision_is_lost_only_when_a_result_needs_more_digits_than_decimal_holds()
    {
        Money tiny = new(0.1234567890123456789012345678m, Currency.DKK);   // 28 decimal places
        Money larger = new(12.345m, Currency.DKK);

        // 12.4684567890123456789012345678 would need 30 significant digits, so it is rounded to fit and
        // the original cannot be recovered.
        Assert.NotEqual(tiny, tiny + larger - larger);

        // Scaling the same amount stays inside the significand, so it round-trips exactly.
        Assert.Equal(tiny, tiny * 3m / 3m);

        // And nothing is lost when the result fits, however extreme the inputs otherwise are.
        Assert.Equal(tiny, tiny + Money.Zero(Currency.DKK));
        Assert.Equal(tiny, -(-tiny));
        Assert.Equal(Money.Zero(Currency.DKK), tiny - tiny);
    }

    /// <summary>
    /// The fifth-unit currencies, called out by name because they are the ones every implementation
    /// gets wrong and the ones the hand-written tests missed.
    /// </summary>
    [Theory]
    [InlineData("MRU", 1.37, 1.4)]   // 1.37 is not a whole khoum; the payable amount is 1.4
    [InlineData("MRU", 1.30, 1.2)]   // 6.5 khoums, exactly between — banker's rounding takes the even 6
    [InlineData("MRU", 1.50, 1.6)]   // 7.5 khoums — ToEven takes 8, so the tie breaks the other way
    [InlineData("MRU", 0.05, 0.0)]
    [InlineData("MRU", 2.00, 2.0)]
    [InlineData("MGA", 1.37, 1.0)]   // MGA cash is whole ariary
    [InlineData("MGA", 1.60, 2.0)]
    public void Fifth_unit_currencies_round_cash_to_a_payable_amount(string code, double raw, double expected)
    {
        Currency currency = Currency.FromCode(code);
        Money rounded = new Money((decimal)raw, currency).RoundToCash();

        Assert.Equal((decimal)expected, rounded.Amount);
        Assert.True(rounded.IsCanonical);
    }

    /// <summary>
    /// Rounding an amount too large to scale into minor units overflows rather than silently returning
    /// something wrong.
    /// </summary>
    /// <remarks>
    /// Only reachable for a currency whose minor unit is not a power of ten, since those are the ones
    /// that scale by hand. An <see cref="OverflowException"/> is the correct outcome — the honest answer
    /// does not fit — and this test exists so that the day it becomes a wrong number instead, something
    /// says so.
    /// </remarks>
    [Fact]
    public void Rounding_overflows_rather_than_returning_a_wrong_answer()
    {
        Money enormous = new(decimal.MaxValue, Currency.MRU);

        Assert.Throws<OverflowException>(() => enormous.Round());

        // IsCanonical is a property, so it answers instead of throwing.
        Assert.False(enormous.IsCanonical);
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
