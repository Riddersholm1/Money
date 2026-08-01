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

    private static Currency[]? _allCurrencies;
    private static FrozenDictionary<short, Currency>? _byNumericCode;

    public static ReadOnlySpan<Currency> AllCurrencies => _allCurrencies ??= BuildAll();

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

        if (CurrencyRegistry.TryGet(packed, out CurrencyInfo? registered))
        {
            return registered;
        }

        return CreateFallback(packed);
    }

    public static bool TryGetByNumericCode(short numericCode, out Currency currency)
    {
        FrozenDictionary<short, Currency> map = _byNumericCode ??= BuildNumericIndex();
        return map.TryGetValue(numericCode, out currency);
    }

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
            IsKnown = false,
        };
    }

    private static Currency[] BuildAll()
    {
        Currency[] all = new Currency[CurrencyTable.Count];

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
