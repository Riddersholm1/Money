using System.Globalization;
using BenchmarkDotNet.Attributes;

namespace Riddersholm.Money.Benchmarks;

/// <summary>Formatting and parsing, where the span paths should allocate nothing at all.</summary>
[MemoryDiagnoser]
[HideColumns("Error", "StdDev", "Median")]
public class FormattingBenchmarks
{
    private static readonly CultureInfo Invariant = CultureInfo.InvariantCulture;
    private static readonly CultureInfo Danish = new("da-DK");

    private readonly Money _money = new(1234.56m, Currency.DKK);
    private readonly NodaMoney.Money _nodaMoney = new(1234.56m, "DKK");

    private readonly char[] _buffer = new char[64];
    private readonly byte[] _utf8Buffer = new byte[64];

    private const string Text = "1234.56 DKK";
    private static readonly byte[] Utf8Text = System.Text.Encoding.UTF8.GetBytes(Text);

    [Benchmark]
    [BenchmarkCategory("Format")]
    public string Format() =>
        _money.ToString("G", Invariant);

    [Benchmark]
    [BenchmarkCategory("Format")]
    public string Format_NodaMoney() =>
        _nodaMoney.ToString(Invariant);

    [Benchmark]
    [BenchmarkCategory("Format")]
    public string Format_Localised() =>
        _money.ToString("C", Danish);

    /// <summary>The allocation-free path: straight into a caller-owned buffer.</summary>
    [Benchmark]
    [BenchmarkCategory("Format")]
    public int TryFormat()
    {
        _money.TryFormat(_buffer, out int written, "G", Invariant);
        return written;
    }

    [Benchmark]
    [BenchmarkCategory("Format")]
    public int TryFormatUtf8()
    {
        _money.TryFormat(_utf8Buffer, out int written, "G", Invariant);
        return written;
    }

    [Benchmark]
    [BenchmarkCategory("Parse")]
    public Money Parse() =>
        Money.Parse(Text, Invariant);

    [Benchmark]
    [BenchmarkCategory("Parse")]
    public NodaMoney.Money Parse_NodaMoney() =>
        NodaMoney.Money.Parse(Text, Invariant);

    [Benchmark]
    [BenchmarkCategory("Parse")]
    public Money ParseSpan() =>
        Money.Parse(Text.AsSpan(), Invariant);

    [Benchmark]
    [BenchmarkCategory("Parse")]
    public Money ParseUtf8() =>
        Money.Parse(Utf8Text, Invariant);

    [Benchmark]
    [BenchmarkCategory("Parse")]
    public Currency ParseCurrency() =>
        Currency.Parse("DKK", Invariant);
}
