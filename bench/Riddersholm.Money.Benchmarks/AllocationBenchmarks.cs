using BenchmarkDotNet.Attributes;
// NodaMoney exposes its allocation as an extension method on MoneyExtensions.
using NodaMoney;

namespace Riddersholm.Money.Benchmarks;

/// <summary>Splitting an amount so the parts sum back exactly.</summary>
[MemoryDiagnoser]
[HideColumns("Error", "StdDev", "Median")]
public class AllocationBenchmarks
{
    private readonly Money _money = new(1000m, Currency.DKK);
    private readonly NodaMoney.Money _nodaMoney = new(1000m, "DKK");

    private readonly Money[] _destination = new Money[64];

    private int[] _ratios = [];

    [Params(3, 12, 64)]
    public int Recipients { get; set; }

    /// <summary>
    /// Weights are built to match <see cref="Recipients"/>. A fixed-size array would make this
    /// benchmark's three rows identical while appearing to measure how the split scales.
    /// </summary>
    [GlobalSetup]
    public void Setup() =>
        _ratios = [.. Enumerable.Range(1, Recipients)];

    [Benchmark]
    public Money[] Allocate() =>
        _money.Allocate(Recipients);

    /// <remarks>
    /// NodaMoney returns a lazy sequence, so it is enumerated here to make the comparison fair —
    /// otherwise the benchmark would measure building an iterator rather than splitting an amount.
    /// </remarks>
    [Benchmark]
    public NodaMoney.Money[] Allocate_NodaMoney() =>
        [.. _nodaMoney.Split(Recipients)];

    /// <summary>The allocation-free overload: results go into a buffer the caller already owns.</summary>
    [Benchmark]
    public Money AllocateIntoSpan()
    {
        Span<Money> destination = _destination.AsSpan(0, Recipients);
        _money.Allocate(destination);
        return destination[0];
    }

    [Benchmark]
    public Money[] AllocateByRatio() =>
        _money.Allocate(_ratios);
}
