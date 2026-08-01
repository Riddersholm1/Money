using System.Numerics;
using FsCheck;
using FsCheck.Fluent;
using FsCheck.Xunit;
using Xunit;

namespace Riddersholm.Money.Tests;

/// <summary>
/// Checks allocation and rounding against a second implementation written from the specification in
/// exact integer arithmetic.
/// </summary>
/// <remarks>
/// <para>
/// The other tests assert that the library agrees with itself. This one asserts that it agrees with
/// something else — an oracle built from <see cref="BigInteger"/>, with no <see cref="decimal"/>
/// anywhere in it, derived from what <c>docs/allocation.md</c> says rather than from how
/// <c>Money.Allocation.cs</c> does it.
/// </para>
/// <para>
/// That distinction is the point. An oracle that shares the implementation's arithmetic would confirm
/// its rounding behaviour rather than check it, so agreement would prove nothing. Here the two share
/// only the specification, and every disagreement is a real defect in one of them.
/// </para>
/// </remarks>
public sealed class AllocationOracleTests
{
    /// <summary>Currencies covering every shape of minor unit: powers of ten, none, and fifths.</summary>
    public static readonly Currency[] Currencies =
    [
        Currency.DKK,   // 100
        Currency.JPY,   // 1
        Currency.KWD,   // 1000
        Currency.CLF,   // 10000
        Currency.MRU,   // 5
        Currency.MGA,   // 5
    ];

    [Property(MaxTest = 500)]
    public Property Equal_allocation_matches_the_integer_oracle()
    {
        return Prop.ForAll(
            MinorUnitAmounts().ToArbitrary(),
            Gen.Choose(1, 64).ToArbitrary(),
            (sample, count) =>
            {
                Money total = sample.Money;
                Money[] actual = total.Allocate(count);

                BigInteger[] expected = AllocateEvenly(sample.MinorUnits, count);

                return actual.Length == expected.Length
                    && actual.Select(m => ToMinorUnits(m, sample.Currency)).SequenceEqual(expected);
            });
    }

    [Property(MaxTest = 500)]
    public Property Ratio_allocation_matches_the_integer_oracle()
    {
        return Prop.ForAll(
            MinorUnitAmounts().ToArbitrary(),
            Gen.NonEmptyListOf(Gen.Choose(0, 1000)).Where(r => r.Sum() > 0).ToArbitrary(),
            (sample, ratios) =>
            {
                int[] weights = [.. ratios];
                Money[] actual = sample.Money.Allocate(weights);

                BigInteger[] expected = AllocateByRatio(sample.MinorUnits, weights);

                return actual.Length == expected.Length
                    && actual.Select(m => ToMinorUnits(m, sample.Currency)).SequenceEqual(expected);
            });
    }

    [Property(MaxTest = 500)]
    public Property Rounding_matches_the_integer_oracle()
    {
        return Prop.ForAll(
            RawAmounts().ToArbitrary(),
            (sample) =>
            {
                Money rounded = sample.Money.Round(MidpointRounding.AwayFromZero);
                BigInteger expected = RoundHalfAwayFromZero(sample.Numerator, sample.Denominator);

                return ToMinorUnits(rounded, sample.Currency) == expected;
            });
    }

    /// <summary>
    /// The counterexample the oracle found, pinned so it can never come back.
    /// </summary>
    /// <remarks>
    /// Recipients 2 and 17 have exactly equal shortfalls — <c>757197 × 247</c> and <c>757197 × 388</c>
    /// both leave 4977 against a total of 8883 — and the documented rule is that the earlier position
    /// takes the spare unit. Computing the shortfalls as rounded decimals put them in the wrong order,
    /// so recipient 17 won. The total was still exact and no part was off by more than one yen, which
    /// is precisely why nothing else caught it.
    /// </remarks>
    [Fact]
    public void A_tie_in_the_largest_remainder_method_goes_to_the_earlier_position()
    {
        int[] weights = [355, 245, 247, 228, 989, 812, 75, 720, 816, 872, 551, 641, 234, 349, 546, 128, 611, 388, 76];
        Money total = new(757_197m, Currency.JPY);

        Money[] parts = total.Allocate(weights);

        Assert.Equal(total, parts.Sum());
        Assert.Equal(21_055m, parts[2].Amount);   // the tie-breaker, and the yen that used to go astray
        Assert.Equal(33_073m, parts[17].Amount);

        Assert.Equal(
            [.. AllocateByRatio(757_197, weights).Select(b => (decimal)b)],
            [.. parts.Select(p => p.Amount)]);
    }

    /// <summary>The same defect, negative, where truncation runs the other way.</summary>
    [Fact]
    public void A_tie_is_broken_the_same_way_for_a_negative_total()
    {
        int[] weights = [664, 236, 995, 64, 350, 859, 4, 139];
        Money total = new(-247_401m, Currency.JPY);

        Money[] parts = total.Allocate(weights);

        Assert.Equal(total, parts.Sum());
        Assert.Equal(
            [.. AllocateByRatio(-247_401, weights).Select(b => (decimal)b)],
            [.. parts.Select(p => p.Amount)]);
    }

    /// <summary>
    /// Weights large enough to overflow the fast path still allocate exactly, on the
    /// <see cref="BigInteger"/> fallback.
    /// </summary>
    [Fact]
    public void Enormous_and_fractional_weights_fall_back_without_losing_exactness()
    {
        Money total = new(1_000_000_000m, Currency.DKK);

        // Passing raw amounts as weights is natural, and 10^18 is past what Int128 can multiply by
        // 10^11 minor units.
        Money[] parts = total.Allocate([1m, 1_000_000_000_000_000_000m, 12345.6789m]);

        Assert.Equal(total, parts.Sum());
        Assert.All(parts, part => Assert.True(part.IsCanonical));

        // Fractional weights are scaled to whole numbers, so proportions are preserved exactly.
        Money[] halves = new Money(100m, Currency.DKK).Allocate([0.5m, 0.5m]);

        Assert.Equal([50m, 50m], [.. halves.Select(p => p.Amount)]);

        Money[] thirds = new Money(100m, Currency.DKK).Allocate([1m, 1m, 1m]);

        Assert.Equal(new Money(100m, Currency.DKK), thirds.Sum());
        Assert.Equal([33.34m, 33.33m, 33.33m], [.. thirds.Select(p => p.Amount)]);
    }

    // ---- The oracle. No decimal appears below this line. --------------------------------------

    /// <summary>
    /// Splits <paramref name="units"/> into <paramref name="count"/> parts, giving the earlier parts
    /// the remainder one unit each, per <c>docs/allocation.md</c>.
    /// </summary>
    /// <remarks>
    /// <see cref="BigInteger"/> division truncates toward zero and its remainder takes the dividend's
    /// sign, which is exactly the symmetric-under-negation behaviour the documentation specifies.
    /// </remarks>
    private static BigInteger[] AllocateEvenly(BigInteger units, int count)
    {
        BigInteger each = units / count;
        BigInteger remainder = units - (each * count);
        int extra = (int)BigInteger.Abs(remainder);
        BigInteger step = remainder.Sign;

        BigInteger[] parts = new BigInteger[count];

        for (int i = 0; i < count; i++)
        {
            parts[i] = i < extra ? each + step : each;
        }

        return parts;
    }

    /// <summary>
    /// The largest-remainder method: truncate each exact share, then hand the leftover units to
    /// whoever was shortchanged most, ties going to the earlier position.
    /// </summary>
    /// <remarks>
    /// Shortfalls are compared as exact rationals — <c>units * weight</c> against a common denominator
    /// of <c>total</c> — so there is no rounding anywhere in the comparison. The implementation reaches
    /// the same ordering through <see cref="decimal"/>, and whether those two agree is the question.
    /// </remarks>
    private static BigInteger[] AllocateByRatio(BigInteger units, int[] weights)
    {
        BigInteger total = weights.Aggregate(BigInteger.Zero, (sum, w) => sum + w);
        int count = weights.Length;

        BigInteger[] shares = new BigInteger[count];
        BigInteger[] shortfalls = new BigInteger[count];
        BigInteger assigned = BigInteger.Zero;

        for (int i = 0; i < count; i++)
        {
            BigInteger scaled = units * weights[i];
            BigInteger share = scaled / total;            // truncates toward zero, as the spec says

            shares[i] = share;
            shortfalls[i] = BigInteger.Abs(scaled - (share * total));
            assigned += share;
        }

        BigInteger remainder = units - assigned;
        BigInteger step = remainder.Sign;

        for (int outstanding = (int)BigInteger.Abs(remainder); outstanding > 0; outstanding--)
        {
            int best = 0;

            for (int i = 1; i < count; i++)
            {
                if (shortfalls[i] > shortfalls[best])
                {
                    best = i;
                }
            }

            shares[best] += step;
            shortfalls[best] = BigInteger.MinusOne;
        }

        return shares;
    }

    /// <summary>Rounds the rational <c>numerator / denominator</c> to a whole number, halves away from zero.</summary>
    private static BigInteger RoundHalfAwayFromZero(BigInteger numerator, BigInteger denominator)
    {
        BigInteger sign = numerator.Sign < 0 ? BigInteger.MinusOne : BigInteger.One;
        BigInteger magnitude = BigInteger.Abs(numerator);

        // (2n + d) / 2d, floored, is n/d rounded with halves going up.
        BigInteger rounded = ((2 * magnitude) + denominator) / (2 * denominator);

        return sign * rounded;
    }

    // ---- Generators ---------------------------------------------------------------------------

    private sealed record MinorUnitSample(Money Money, Currency Currency, BigInteger MinorUnits);

    private sealed record RawSample(Money Money, Currency Currency, BigInteger Numerator, BigInteger Denominator);

    /// <summary>Canonical amounts, described both as <see cref="Money"/> and as whole minor units.</summary>
    private static Gen<MinorUnitSample> MinorUnitAmounts() =>
        from currency in Gen.Elements(Currencies)
        from units in Gen.Choose(-1_000_000, 1_000_000)
        select new MinorUnitSample(
            new Money(units / (decimal)currency.MinorUnitsPerMajor, currency),
            currency,
            units);

    /// <summary>
    /// Arbitrary amounts with fractional minor units, described as an exact rational so the oracle can
    /// round them without ever touching a <see cref="decimal"/>.
    /// </summary>
    private static Gen<RawSample> RawAmounts() =>
        from currency in Gen.Elements(Currencies)
        from thousandthsOfAUnit in Gen.Choose(-1_000_000, 1_000_000)
        let unitsPerMajor = currency.MinorUnitsPerMajor
        // The amount is thousandthsOfAUnit / 1000 minor units, i.e. that over 1000 * unitsPerMajor major.
        select new RawSample(
            new Money(thousandthsOfAUnit / (1000m * unitsPerMajor), currency),
            currency,
            thousandthsOfAUnit,
            1000);

    private static BigInteger ToMinorUnits(Money money, Currency currency)
    {
        decimal units = money.Amount * currency.MinorUnitsPerMajor;

        Assert.Equal(decimal.Truncate(units), units);

        return new BigInteger(units);
    }
}
