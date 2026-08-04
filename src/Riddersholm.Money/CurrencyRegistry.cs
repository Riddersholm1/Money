using System.Collections.Frozen;

namespace Riddersholm.Money;

/// <summary>
/// Holds metadata for currencies that were not compiled into the library — cryptocurrencies, in-game
/// currencies, historic codes, or anything else an application needs.
/// </summary>
/// <remarks>
/// <para>
/// Most applications never call this directly. Reference <c>Riddersholm.Money.Generators</c>, point it
/// at a JSON definition file, and the generator emits both the strongly typed members and the
/// registration call — see the package README.
/// </para>
/// <para>
/// Registration exists only to supply <em>metadata</em>. A <see cref="Currency"/> carries its own code,
/// so <see cref="Currency.FromCode(string)"/>, equality, formatting, parsing, and persistence all work
/// for unregistered currencies too; registering adds the name, symbol, and precision.
/// </para>
/// <para>
/// The table is replaced atomically rather than mutated, so lookups are lock-free and never observe a
/// half-built dictionary. ISO currencies cannot be overridden.
/// </para>
/// </remarks>
public static class CurrencyRegistry
{
    private static FrozenDictionary<uint, CurrencyInfo> CustomCurrencies = FrozenDictionary<uint, CurrencyInfo>.Empty;

    /// <summary>Metadata for every currency registered at runtime, in no particular order.</summary>
    public static IReadOnlyCollection<CurrencyInfo> Custom => CustomCurrencies.Values;

    /// <summary>Registers metadata for currencies the library does not know at compile time.</summary>
    /// <param name="currencies">The metadata to publish.</param>
    /// <remarks>
    /// Re-registering a currency with identical metadata is a no-op, so two assemblies may safely
    /// declare the same custom currency.
    /// </remarks>
    /// <exception cref="ArgumentException">
    /// A currency is already defined by ISO 4217, or is already registered with different metadata.
    /// Overriding a currency's precision silently would corrupt every amount using it.
    /// </exception>
    public static void Register(params ReadOnlySpan<CurrencyInfo> currencies)
    {
        if (currencies.IsEmpty)
        {
            return;
        }

        foreach (CurrencyInfo info in currencies)
        {
            ArgumentNullException.ThrowIfNull(info);

            if (CurrencyTable.TryGetOrdinal(info.Currency.PackedValue, out _))
            {
                throw new ArgumentException(
                    $"'{info.Code}' is an ISO 4217 currency and cannot be redefined.",
                    nameof(currencies));
            }
        }

        // Swap in a new frozen table rather than mutating a shared one: readers stay lock-free and can
        // never see a partially populated dictionary.
        while (true)
        {
            FrozenDictionary<uint, CurrencyInfo> current = CustomCurrencies;
            Dictionary<uint, CurrencyInfo> updated = new(current.Count + currencies.Length);

            foreach (KeyValuePair<uint, CurrencyInfo> entry in current)
            {
                updated[entry.Key] = entry.Value;
            }

            bool changed = false;

            foreach (CurrencyInfo info in currencies)
            {
                uint packed = info.Currency.PackedValue;

                if (updated.TryGetValue(packed, out CurrencyInfo? existing))
                {
                    if (!Matches(existing, info))
                    {
                        throw new ArgumentException(
                            $"'{info.Code}' is already registered with different metadata.",
                            nameof(currencies));
                    }

                    continue;
                }

                updated[packed] = info;
                changed = true;
            }

            if (!changed)
            {
                return;
            }

            var replacement = updated.ToFrozenDictionary();

            if (ReferenceEquals(Interlocked.CompareExchange(ref CustomCurrencies, replacement, current), current))
            {
                return;
            }

            // Another thread registered concurrently; rebuild on top of its result and retry.
        }
    }

    /// <summary>Looks up runtime-registered metadata.</summary>
    internal static bool TryGet(uint packed, out CurrencyInfo info) => CustomCurrencies.TryGetValue(packed, out info!);

    private static bool Matches(CurrencyInfo left, CurrencyInfo right) =>
        left.NumericCode == right.NumericCode
        && left.DecimalDigits == right.DecimalDigits
        && left.MinorUnitsPerMajor == right.MinorUnitsPerMajor
        && left.CashDecimalDigits == right.CashDecimalDigits
        && left.CashRoundingStep == right.CashRoundingStep
        && string.Equals(left.EnglishName, right.EnglishName, StringComparison.Ordinal)
        && string.Equals(left.Symbol, right.Symbol, StringComparison.Ordinal);
}
