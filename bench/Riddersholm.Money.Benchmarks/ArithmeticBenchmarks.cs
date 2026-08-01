using BenchmarkDotNet.Attributes;

namespace Riddersholm.Money.Benchmarks;

/// <summary>
/// Creation and arithmetic, against NodaMoney and against a bare <see cref="decimal"/>.
/// </summary>
/// <remarks>
/// The <c>decimal</c> rows are the interesting baseline: they show what the currency check and the
/// wider struct actually cost over doing the arithmetic with no safety at all.
/// </remarks>
[MemoryDiagnoser]
[HideColumns("Error", "StdDev", "Median")]
public class ArithmeticBenchmarks
{
    private readonly Money _left = new(100.50m, Currency.DKK);
    private readonly Money _right = new(50.25m, Currency.DKK);

    private readonly NodaMoney.Money _nodaLeft = new(100.50m, "DKK");
    private readonly NodaMoney.Money _nodaRight = new(50.25m, "DKK");

    private readonly decimal _decimalLeft = 100.50m;
    private readonly decimal _decimalRight = 50.25m;

    /// <remarks>
    /// The amount comes from a field rather than a literal. With a constant the JIT folds the whole
    /// construction away and BenchmarkDotNet reports a duration indistinguishable from an empty
    /// method — a number that says nothing except that the optimiser works.
    /// </remarks>
    [Benchmark, BenchmarkCategory("Create")]
    public Money Create() => new(_decimalLeft, Currency.DKK);

    [Benchmark, BenchmarkCategory("Create")]
    public NodaMoney.Money Create_NodaMoney() => new(100.50m, "DKK");

    [Benchmark, BenchmarkCategory("Create")]
    public Currency Create_CurrencyFromCode() => Currency.FromCode("DKK");

    [Benchmark, BenchmarkCategory("Add")]
    public Money Add() => _left + _right;

    [Benchmark, BenchmarkCategory("Add")]
    public NodaMoney.Money Add_NodaMoney() => _nodaLeft + _nodaRight;

    [Benchmark, BenchmarkCategory("Add")]
    public decimal Add_Decimal() => _decimalLeft + _decimalRight;

    [Benchmark, BenchmarkCategory("Subtract")]
    public Money Subtract() => _left - _right;

    [Benchmark, BenchmarkCategory("Subtract")]
    public NodaMoney.Money Subtract_NodaMoney() => _nodaLeft - _nodaRight;

    [Benchmark, BenchmarkCategory("Multiply")]
    public Money Multiply() => _left * 2.5m;

    [Benchmark, BenchmarkCategory("Multiply")]
    public NodaMoney.Money Multiply_NodaMoney() => _nodaLeft * 2.5m;

    [Benchmark, BenchmarkCategory("Divide")]
    public Money Divide() => _left / 4m;

    [Benchmark, BenchmarkCategory("Divide")]
    public NodaMoney.Money Divide_NodaMoney() => _nodaLeft / 4m;

    [Benchmark, BenchmarkCategory("Compare")]
    public bool Equals_() => _left == _right;

    [Benchmark, BenchmarkCategory("Compare")]
    public bool Equals_NodaMoney() => _nodaLeft == _nodaRight;

    [Benchmark, BenchmarkCategory("Round")]
    public Money Round() => _left.Round();

    [Benchmark, BenchmarkCategory("Round")]
    public decimal Round_Decimal() => Math.Round(_decimalLeft, 2, MidpointRounding.ToEven);

    /// <summary>
    /// Reading a currency's precision must not allocate or trigger a static constructor: it is on the
    /// rounding path, which is on everyone's hot path.
    /// </summary>
    [Benchmark, BenchmarkCategory("Metadata")]
    public int ReadPrecision() => Currency.DKK.DecimalDigits;

    [Benchmark, BenchmarkCategory("Metadata")]
    public string ReadSymbol() => Currency.DKK.Symbol;
}
