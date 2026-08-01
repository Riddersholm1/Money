using System.Globalization;
using BenchmarkDotNet.Attributes;

namespace Riddersholm.Money.Benchmarks;

/// <summary>
/// Formatting once the derived-format cache has filled up, which is the state the cache's size limit
/// exists to produce and the one nothing else measures.
/// </summary>
/// <remarks>
/// <para>
/// <c>CurrencyFormatCache</c> memoises at most 1024 derived <see cref="NumberFormatInfo"/> instances so
/// that an application formatting attacker-influenced culture/currency pairs cannot grow it without
/// bound. Past the cap, formatting still works — it just stops memoising, so every call for an uncached
/// pair takes the miss path.
/// </para>
/// <para>
/// That makes the miss path worth measuring. An earlier version tested the cap with
/// <c>ConcurrentDictionary.Count</c>, which acquires every bucket lock; once the cache filled, each
/// subsequent format call took all of them. This benchmark is the regression guard: if the gap between
/// <see cref="CacheHit"/> and <see cref="CacheMissPastTheCap"/> ever widens to hundreds of nanoseconds,
/// the cap check has started locking again.
/// </para>
/// </remarks>
[MemoryDiagnoser]
[HideColumns("Error", "StdDev", "Median")]
public class FormatCacheBenchmarks
{
    /// <summary>Mirrors <c>CurrencyFormatCache.MaximumCachedFormats</c>, which is internal.</summary>
    private const int CacheCapacity = 1024;

    private CultureInfo _cachedCulture = CultureInfo.InvariantCulture;
    private CultureInfo _uncachedCulture = CultureInfo.InvariantCulture;

    private Money _cachedMoney;
    private Money _uncachedMoney;

    [GlobalSetup]
    public void FillTheCache()
    {
        CultureInfo[] cultures = CultureInfo.GetCultures(CultureTypes.SpecificCultures);
        Currency[] currencies = [.. Currency.Known];

        if (cultures.Length < 2 || (long)(cultures.Length - 1) * (currencies.Length - 1) <= CacheCapacity)
        {
            throw new InvalidOperationException(
                $"Need more than {CacheCapacity} culture/currency pairs to fill the cache, but this "
              + $"machine has only {cultures.Length} cultures and {currencies.Length} currencies.");
        }

        // The last culture and the last currency are held back from the fill. Once the cache is full it
        // accepts nothing further, so that pair is guaranteed to miss for the rest of the run.
        _cachedCulture = cultures[0];
        _uncachedCulture = cultures[^1];
        _cachedMoney = new Money(1234.56m, currencies[0]);
        _uncachedMoney = new Money(1234.56m, currencies[^1]);

        int filled = 0;

        for (int c = 0; c < cultures.Length - 1 && filled <= CacheCapacity; c++)
        {
            for (int m = 0; m < currencies.Length - 1 && filled <= CacheCapacity; m++)
            {
                _ = new Money(1m, currencies[m]).ToString("C", cultures[c]);
                filled++;
            }
        }
    }

    /// <summary>The ordinary path: the pair was memoised on its first use.</summary>
    [Benchmark(Baseline = true)]
    public string CacheHit() => _cachedMoney.ToString("C", _cachedCulture);

    /// <summary>
    /// The path taken by every format call for a pair that arrived after the cache filled. It derives a
    /// <see cref="NumberFormatInfo"/> each time — and must not do anything worse than that.
    /// </summary>
    [Benchmark]
    public string CacheMissPastTheCap() => _uncachedMoney.ToString("C", _uncachedCulture);
}
