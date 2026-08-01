using System.Collections.Concurrent;
using System.Globalization;

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
/// logic in the BCL, which is where it belongs. Results are cached per culture and currency so that
/// formatting allocates nothing after the first call.
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

    private static readonly ConcurrentDictionary<(string Culture, uint Currency), NumberFormatInfo> Formats = new();

    /// <summary>Approximate entry count, maintained so the cap check never has to lock the dictionary.</summary>
    private static int _count;

    /// <summary>ISO currency code per culture name; <see langword="null"/> when the culture has no region.</summary>
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
            _ => null,
        };

        if (culture is null)
        {
            return Build(NumberFormatInfo.GetInstance(provider), culture: null, currency, decimalDigits);
        }

        // An explicit digit count is per-call and not worth caching against.
        if (decimalDigits is not null)
        {
            return Build(culture.NumberFormat, culture, currency, decimalDigits);
        }

        if (Formats.TryGetValue((culture.Name, currency.PackedValue), out NumberFormatInfo? cached))
        {
            return cached;
        }

        NumberFormatInfo built = Build(culture.NumberFormat, culture, currency, null);

        // Bounded on purpose. The key space is cultures × currencies — on the order of 10^5 — and an
        // application that formats attacker-influenced pairs could otherwise grow this without limit.
        // Past the cap, formatting still works; it just stops memoising.
        //
        // The count is tracked separately rather than read from the dictionary:
        // ConcurrentDictionary.Count acquires every bucket lock, and this runs on each miss, so using
        // it would put a lock convoy on the formatting path the moment the cache filled up — a worse
        // failure than the unbounded growth the cap exists to prevent.
        if (Volatile.Read(ref _count) >= MaximumCachedFormats)
        {
            return built;
        }

        if (Formats.TryAdd((culture.Name, currency.PackedValue), built))
        {
            Interlocked.Increment(ref _count);
            return built;
        }

        // Another thread cached an equivalent instance first; prefer the shared one.
        return Formats.TryGetValue((culture.Name, currency.PackedValue), out NumberFormatInfo? raced)
            ? raced
            : built;
    }

    private static NumberFormatInfo Build(
        NumberFormatInfo source,
        CultureInfo? culture,
        Currency currency,
        int? decimalDigits)
    {
        NumberFormatInfo format = (NumberFormatInfo)source.Clone();

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
