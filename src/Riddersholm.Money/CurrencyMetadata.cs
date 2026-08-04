using System.Collections.Frozen;

namespace Riddersholm.Money;

/// <summary>
/// Materialises <see cref="CurrencyInfo"/> objects on demand and caches them.
/// </summary>
/// <remarks>
/// Nothing here runs unless a caller actually asks for descriptive metadata or enumerates
/// <see cref="Currency.Known"/>. Arithmetic, comparison, rounding, and formatting read the generated
/// scalar tables directly, so a program that only moves money around never allocates a single
/// <see cref="CurrencyInfo"/>.
/// </remarks>
internal static class CurrencyMetadata
{
    private static readonly CurrencyInfo?[] Cache = new CurrencyInfo?[CurrencyTable.Count];

    private static Currency[]? CachedCurrencies;
    private static FrozenDictionary<short, Currency>? CachedNumericIndex;

    /// <remarks>
    /// Published through <see cref="Interlocked"/> like <see cref="Cache"/> below, rather than with a
    /// plain <c>??=</c>. A race would only ever build a second, equal table, but this returns a
    /// <see cref="ReadOnlySpan{T}"/> over the array's interior — so the guarantee that matters is that
    /// no thread can observe the reference before the elements it points at, and stating that with a
    /// fence is cheaper than arguing about which runtimes provide it for free.
    /// </remarks>
    public static ReadOnlySpan<Currency> AllCurrencies =>
        Volatile.Read(ref CachedCurrencies) ?? Publish(ref CachedCurrencies, BuildAll());

    public static CurrencyInfo Get(uint packed)
    {
        if (CurrencyTable.TryGetOrdinal(packed, out int ordinal))
        {
            CurrencyInfo? cached = Volatile.Read(ref Cache[ordinal]);

            if (cached is not null)
            {
                return cached;
            }

            CurrencyInfo created = Create(ordinal);

            // A race here would only produce two equal instances, but resolving it keeps reference
            // identity stable, which callers reasonably expect from a metadata lookup.
            return Interlocked.CompareExchange(ref Cache[ordinal], created, null) ?? created;
        }

        return CurrencyRegistry.TryGet(packed, out CurrencyInfo registered)
            ? registered
            : CreateFallback(packed);
    }

    public static bool TryGetByNumericCode(short numericCode, out Currency currency)
    {
        FrozenDictionary<short, Currency> map = Volatile.Read(ref CachedNumericIndex) ?? Publish(ref CachedNumericIndex, BuildNumericIndex());

        return map.TryGetValue(numericCode, out currency);
    }

    /// <summary>
    /// Publishes a lazily built table, returning whichever instance won if two threads built one.
    /// </summary>
    /// <remarks>
    /// Returning the winner rather than the caller's own instance keeps reference identity stable, so
    /// two calls never hand back tables that are equal but not the same object.
    /// </remarks>
    private static T Publish<T>(ref T? location, T built)
        where T : class =>
        Interlocked.CompareExchange(ref location, built, null) ?? built;

    private static CurrencyInfo Create(int ordinal) => new(
        code: CurrencyTable.GetCode(ordinal),
        numericCode: CurrencyTable.GetNumericCode(ordinal),
        englishName: CurrencyTable.GetName(ordinal),
        symbol: CurrencyTable.GetSymbol(ordinal),
        decimalDigits: CurrencyTable.DecimalDigits[ordinal],
        minorUnitsPerMajor: CurrencyTable.GetMinorUnitsPerMajor(ordinal),
        cashDecimalDigits: CurrencyTable.CashDecimalDigits[ordinal],
        cashRoundingStep: CurrencyTable.CashRoundingSteps[ordinal]);

    /// <summary>
    /// Describes a currency the library has never seen, so that loading unfamiliar data reads its
    /// metadata instead of throwing. The precision is the ISO default and is explicitly not trustworthy,
    /// which is why <see cref="CurrencyInfo.IsKnown"/> is <see langword="false"/> and
    /// <see cref="Money.Round(System.MidpointRounding)"/> refuses to act on it.
    /// </summary>
    private static CurrencyInfo CreateFallback(uint packed)
    {
        string code = CurrencyCodec.Decode(packed);

        return new CurrencyInfo(
            code: code,
            numericCode: 0,
            englishName: code,
            symbol: code,
            decimalDigits: 2,
            minorUnitsPerMajor: 100,
            cashDecimalDigits: 2,
            cashRoundingStep: 1)
        {
            IsKnown = false
        };
    }

    private static Currency[] BuildAll()
    {
        var all = new Currency[CurrencyTable.Count];

        for (int i = 0; i < all.Length; i++)
        {
            all[i] = new Currency(CurrencyTable.GetPacked(i));
        }

        return all;
    }

    private static FrozenDictionary<short, Currency> BuildNumericIndex()
    {
        Dictionary<short, Currency> map = new(CurrencyTable.Count);

        for (int i = 0; i < CurrencyTable.Count; i++)
        {
            map[CurrencyTable.GetNumericCode(i)] = new Currency(CurrencyTable.GetPacked(i));
        }

        return map.ToFrozenDictionary();
    }
}
