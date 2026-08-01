using System.Collections.Concurrent;
using System.Globalization;
using Xunit;

namespace Riddersholm.Money.Tests;

/// <summary>
/// The library advertises thread safety, and its only mutable state — the currency registry and the
/// lazily built metadata caches — was entirely unexercised under concurrency.
/// </summary>
public sealed class ThreadSafetyTests
{
    [Fact]
    public void Concurrent_registration_of_distinct_currencies_loses_none()
    {
        // The registry swaps a frozen dictionary under a compare-and-exchange loop. A lost update
        // would silently drop somebody's currency.
        string[] codes = [.. Enumerable.Range(0, 40).Select(i => $"T{(char)('A' + (i / 26))}{(char)('A' + (i % 26))}")];

        Parallel.ForEach(codes, code =>
            CurrencyRegistry.Register(new CurrencyInfo(code, 0, $"Test {code}", code, 2, 100L, 2, 1)));

        foreach (string code in codes)
        {
            Currency currency = Currency.FromCode(code);

            Assert.True(currency.IsKnown, $"'{code}' was lost by a concurrent registration.");
            Assert.Equal($"Test {code}", currency.EnglishName);
        }
    }

    [Fact]
    public void Concurrent_registration_of_the_same_currency_is_safe()
    {
        CurrencyInfo info = new("TZZ", 0, "Repeated", "TZZ", 2, 100L, 2, 1);

        Parallel.For(0, 64, _ => CurrencyRegistry.Register(info));

        Assert.Equal("Repeated", Currency.FromCode("TZZ").EnglishName);
    }

    [Fact]
    public void Concurrent_metadata_reads_return_one_shared_instance()
    {
        // CurrencyInfo objects are cached with Interlocked.CompareExchange so that reference identity
        // stays stable, which callers reasonably expect of a metadata lookup.
        ConcurrentBag<CurrencyInfo> observed = [];

        Parallel.For(0, 128, _ => observed.Add(Currency.ZAR.Info));

        Assert.Single(observed.Distinct(ReferenceEqualityComparer.Instance));
    }

    [Fact]
    public void Concurrent_first_use_of_the_currency_table_is_consistent()
    {
        ConcurrentBag<int> lengths = [];

        Parallel.For(0, 128, _ => lengths.Add(Currency.Known.Length));

        Assert.Equal(166, Assert.Single(lengths.Distinct()));
    }

    [Fact]
    public void Concurrent_numeric_lookups_agree()
    {
        Parallel.For(0, 256, _ =>
        {
            Assert.True(Currency.TryFromNumericCode(208, out Currency currency));
            Assert.Equal(Currency.DKK, currency);
        });
    }

    [Fact]
    public void Concurrent_formatting_across_cultures_is_consistent()
    {
        // The derived NumberFormatInfo cache is the one piece of shared mutable state on the
        // formatting path.
        CultureInfo[] cultures = [new("da-DK"), new("en-US"), new("de-DE"), CultureInfo.InvariantCulture];
        Money value = new(1234.5m, Currency.DKK);

        // Each culture's expected output is established single-threaded first; the property under test
        // is that racing readers never observe a half-built derived NumberFormatInfo.
        Dictionary<string, string> expected = cultures.ToDictionary(
            culture => culture.Name,
            culture => value.ToString("C", culture),
            StringComparer.Ordinal);

        ConcurrentBag<(string Culture, string Text)> results = [];

        Parallel.For(0, 512, i =>
        {
            CultureInfo culture = cultures[i % cultures.Length];
            results.Add((culture.Name, value.ToString("C", culture)));
        });

        Assert.All(results, result => Assert.Equal(expected[result.Culture], result.Text));
        Assert.Equal(512, results.Count);
    }

    [Fact]
    public void Concurrent_arithmetic_and_allocation_produce_stable_results()
    {
        Money total = new(1000m, Currency.DKK);

        Parallel.For(0, 512, _ =>
        {
            Money[] parts = total.Allocate(7);

            Assert.Equal(total, parts.Sum());
        });
    }
}
