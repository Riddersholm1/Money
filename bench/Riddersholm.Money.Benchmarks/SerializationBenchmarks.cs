using System.Text.Json;
using BenchmarkDotNet.Attributes;
using Riddersholm.Money.Serialization;

namespace Riddersholm.Money.Benchmarks;

/// <summary>JSON, including the reflection-free source-generated path used under NativeAOT.</summary>
[MemoryDiagnoser]
[HideColumns("Error", "StdDev", "Median")]
public class SerializationBenchmarks
{
    private static readonly JsonSerializerOptions Compact =
        new JsonSerializerOptions().AddMoney(MoneyJsonFormat.Compact);

    private readonly Money _money = new(1234.56m, Currency.DKK);

    private const string Json = """{"amount":1234.56,"currency":"DKK"}""";
    private static readonly byte[] Utf8Json = System.Text.Encoding.UTF8.GetBytes(Json);

    [Benchmark, BenchmarkCategory("Serialize")]
    public string Serialize() => JsonSerializer.Serialize(_money);

    [Benchmark, BenchmarkCategory("Serialize")]
    public string SerializeSourceGenerated() =>
        JsonSerializer.Serialize(_money, MoneyJsonSerializerContext.Default.Money);

    [Benchmark, BenchmarkCategory("Serialize")]
    public byte[] SerializeUtf8() =>
        JsonSerializer.SerializeToUtf8Bytes(_money, MoneyJsonSerializerContext.Default.Money);

    [Benchmark, BenchmarkCategory("Serialize")]
    public string SerializeCompact() => JsonSerializer.Serialize(_money, Compact);

    [Benchmark, BenchmarkCategory("Deserialize")]
    public Money Deserialize() => JsonSerializer.Deserialize<Money>(Json);

    [Benchmark, BenchmarkCategory("Deserialize")]
    public Money DeserializeSourceGenerated() =>
        JsonSerializer.Deserialize(Json, MoneyJsonSerializerContext.Default.Money);

    [Benchmark, BenchmarkCategory("Deserialize")]
    public Money DeserializeUtf8() =>
        JsonSerializer.Deserialize(Utf8Json, MoneyJsonSerializerContext.Default.Money);
}
