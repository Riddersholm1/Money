using System.Collections.Concurrent;
using System.Globalization;
using System.Runtime.CompilerServices;

namespace Riddersholm.Money;

/// <summary>
/// Produces a <see cref="NumberFormatInfo"/> that lays numbers out the caller's way but uses
/// <em>this currency's</em> symbol and precision.
/// </summary>
/// <remarks>
/// <para>
/// The BCL gets this wrong by design. <c>1234m.ToString("C", enUS)</c> is <c>$1,234.00</c> whatever
/// currency you meant, because the culture supplies the precision — so ¥1234 gains two decimal places
/// it does not have and 1.234 KWD loses one it does. Money knows its own precision, so the digit count
/// and symbol come from the currency and everything else — separators, grouping, and the sixteen
/// negative-currency patterns — comes from the provider.
/// </para>
/// <para>
/// Deriving a <see cref="NumberFormatInfo"/> rather than reimplementing those patterns keeps the layout
/// logic in the BCL, which is where it belongs. Results are cached so that formatting allocates nothing
/// after the first call.
/// </para>
/// <para>
/// <b>The cache is keyed on the provider's <see cref="NumberFormatInfo"/> instance, and only a read-only
/// one.</b> An earlier version keyed it on the culture <em>name</em>, which two different formats can
/// share: a clone of <c>da-DK</c> with its separators changed, or a culture built with user overrides,
/// is still called <c>da-DK</c>. Whichever was formatted first won, and every later call under the other
/// silently used its separators — so <c>1.234,50 kr.</c> could come out as <c>1_234#50 kr.</c> for a
/// caller who had never touched the culture. A read-only instance cannot change after it is cached,
/// which makes the instance an exact key. Writable ones are derived afresh on each call; that costs an
/// allocation, and is correct whatever the caller does to the culture between calls. The ambient
/// <see cref="CultureInfo.CurrentCulture"/> and every <see cref="CultureInfo.GetCultureInfo(string)"/>
/// culture are read-only, so the ordinary path stays allocation-free.
/// </para>
/// </remarks>
internal static class CurrencyFormatCache
{
    /// <summary>
    /// How many derived formats to memoise. Comfortably covers any real application — a handful of
    /// cultures against a handful of currencies — while keeping the cache from growing without bound
    /// if the culture or currency is influenced by untrusted input.
    /// </summary>
    private const int MaximumCachedFormats = 1024;

    /// <summary>
    /// Derived formats per source format. Weakly keyed, so a culture the application lets go of takes its
    /// derived formats with it.
    /// </summary>
    /// <remarks>
    /// The inner key records whether the provider was a <see cref="CultureInfo"/>, because the same
    /// read-only <see cref="NumberFormatInfo"/> derives a different symbol when it arrives with its culture
    /// (whose region may own the currency) than when it arrives bare (which names no region).
    /// </remarks>
    private static readonly ConditionalWeakTable<NumberFormatInfo, ConcurrentDictionary<(uint Currency, bool ViaCulture), NumberFormatInfo>> Formats = new();

    /// <summary>Approximate entry count, maintained so the cap check never has to lock anything.</summary>
    private static int Count;

    /// <summary>ISO currency code per culture name; <see langword="null"/> when the culture has no region.</summary>
    /// <remarks>
    /// Keyed on the name deliberately, unlike <see cref="Formats"/>: a culture's region follows from its
    /// name alone, and no setting on a clone can change it.
    /// </remarks>
    private static readonly ConcurrentDictionary<string, string?> RegionCurrencies = new(StringComparer.Ordinal);

    /// <summary>Gets a number format that renders <paramref name="currency"/> correctly.</summary>
    /// <param name="provider">The caller's format provider; <see langword="null"/> means the current culture.</param>
    /// <param name="currency">The currency whose symbol and precision should be used.</param>
    /// <param name="decimalDigits">Overrides the currency's precision when the format string asked for one.</param>
    public static NumberFormatInfo ForCurrency(IFormatProvider? provider, Currency currency, int? decimalDigits)
    {
        CultureInfo? culture = provider switch
        {
            null => CultureInfo.CurrentCulture,
            CultureInfo specified => specified,
            // A bare NumberFormatInfo names no region, so its symbol cannot be trusted to belong to
            // this currency; the currency's own symbol is used instead.
            _ => null
        };

        NumberFormatInfo source = culture?.NumberFormat ?? NumberFormatInfo.GetInstance(provider);

        // An explicit digit count is per-call and not worth caching against, and a writable source can
        // change after it was cached — see the remarks on the class.
        if (decimalDigits is not null || !source.IsReadOnly)
        {
            return Build(source, culture, currency, decimalDigits);
        }

        ConcurrentDictionary<(uint Currency, bool ViaCulture), NumberFormatInfo> derived =
            Formats.GetValue(source, static _ => new ConcurrentDictionary<(uint Currency, bool ViaCulture), NumberFormatInfo>());

        (uint Currency, bool ViaCulture) key = (currency.PackedValue, culture is not null);

        if (derived.TryGetValue(key, out NumberFormatInfo? cached))
        {
            return cached;
        }

        NumberFormatInfo built = Build(source, culture, currency, null);

        // Bounded on purpose. The key space is cultures × currencies — on the order of 10^5 — and an
        // application that formats attacker-influenced pairs could otherwise grow this without limit.
        // Past the cap, formatting still works; it just stops memoising.
        //
        // The count is tracked separately rather than read from the dictionaries: a
        // ConcurrentDictionary's Count acquires every bucket lock, and this runs on each miss, so using
        // it would put a lock convoy on the formatting path the moment the cache filled up — a worse
        // failure than the unbounded growth the cap exists to prevent. It never decreases when a culture
        // is collected, so the cap is conservative rather than exact.
        if (Volatile.Read(ref Count) >= MaximumCachedFormats)
        {
            return built;
        }

        if (derived.TryAdd(key, built))
        {
            Interlocked.Increment(ref Count);
            return built;
        }

        // Another thread cached an equivalent instance first; prefer the shared one.
        return derived.GetValueOrDefault(key, built);
    }

    private static NumberFormatInfo Build(
        NumberFormatInfo source,
        CultureInfo? culture,
        Currency currency,
        int? decimalDigits)
    {
        var format = (NumberFormatInfo)source.Clone();

        // Clone() always returns a writable copy, even when the source is a read-only culture format.
        format.CurrencySymbol = UsesCurrency(culture, currency) ? source.CurrencySymbol : currency.Symbol;
        format.CurrencyDecimalDigits = decimalDigits ?? currency.DecimalDigits;

        return format;
    }

    /// <summary>
    /// Whether the culture's own region uses this currency, in which case its symbol is the more
    /// idiomatic one — <c>da-DK</c> writes <c>kr.</c> where CLDR's culture-neutral form is <c>kr</c>.
    /// </summary>
    private static bool UsesCurrency(CultureInfo? culture, Currency currency) =>
        culture is not null
        && RegionCurrencyCode(culture) is { } code
        && string.Equals(code, currency.Code, StringComparison.Ordinal);

    private static string? RegionCurrencyCode(CultureInfo culture) =>
        RegionCurrencies.GetOrAdd(culture.Name, static name =>
        {
            if (name.Length == 0)
            {
                return null;
            }

            try
            {
                return new RegionInfo(name).ISOCurrencySymbol;
            }
            catch (ArgumentException)
            {
                // Neutral and unrecognised cultures have no region, and so no currency.
                return null;
            }
        });
}
