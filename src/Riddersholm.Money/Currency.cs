using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using System.Runtime.CompilerServices;

namespace Riddersholm.Money;

/// <summary>
/// An ISO 4217 currency, represented as a four-byte identity rather than a string.
/// </summary>
/// <remarks>
/// <para>
/// The three letters of the alphabetic code are packed into a single integer, so equality and hashing
/// are one integer comparison, the struct never allocates, and any well-formed code round-trips
/// exactly — even one this library has never heard of.
/// </para>
/// <para>
/// <c>default(Currency)</c>, <see cref="None"/>, and <c>Currency.XXX</c> are all the same value: ISO
/// reserves <c>XXX</c> for "no currency", and the packed representation reserves zero for it. There is
/// exactly one way to say "no currency", and it serialises as a real ISO code rather than as an empty
/// string.
/// </para>
/// <para>
/// Descriptive metadata lives in <see cref="CurrencyInfo"/>; the common properties are forwarded here
/// for convenience, so <c>Currency.DKK.Symbol</c> works without the metadata living in the struct.
/// </para>
/// </remarks>
/// <example>
/// <code>
/// Currency dkk = Currency.DKK;
/// Currency alsoDkk = Currency.FromCode("DKK");
/// Console.WriteLine(dkk == alsoDkk);        // True
/// Console.WriteLine(dkk.DecimalDigits);     // 2
/// </code>
/// </example>
[DebuggerDisplay("{Code,nq}")]
[System.Text.Json.Serialization.JsonConverter(typeof(Serialization.CurrencyJsonConverter))]
public readonly partial record struct Currency :
    IComparable<Currency>,
    IComparable,
    ISpanFormattable,
    IUtf8SpanFormattable,
    ISpanParsable<Currency>,
    IUtf8SpanParsable<Currency>
{
    /// <summary>
    /// The precision assumed for a currency the library does not recognise. Two digits is the ISO
    /// default and covers 138 of the 166 known currencies.
    /// </summary>
    private const byte FallbackDecimalDigits = 2;

    private const long FallbackMinorUnitsPerMajor = 100L;

    private readonly uint _packed;

    internal Currency(uint packed) => _packed = packed;

    /// <summary>The packed representation, used by the registry and by serialisation.</summary>
    internal uint PackedValue => _packed;

    /// <summary>
    /// The absence of a currency — ISO <c>XXX</c>. Identical to <c>default(Currency)</c> and to
    /// <c>Currency.XXX</c>.
    /// </summary>
    public static Currency None => default;

    /// <summary>The ISO 4217 alphabetic code, for example <c>DKK</c>.</summary>
    /// <remarks>
    /// For known and registered currencies this returns a cached string and allocates nothing. For an
    /// unrecognised code it decodes into a new string; use <see cref="TryFormat(Span{char}, out int, ReadOnlySpan{char}, IFormatProvider?)"/>
    /// on a hot path.
    /// </remarks>
    public string Code =>
        CurrencyTable.TryGetOrdinal(_packed, out int ordinal) ? CurrencyTable.GetCode(ordinal)
        : CurrencyRegistry.TryGet(_packed, out CurrencyInfo? info) ? info.Code
        : CurrencyCodec.Decode(_packed);

    /// <summary>Whether this is <see cref="None"/> — that is, ISO <c>XXX</c>.</summary>
    public bool IsNone => _packed == CurrencyCodec.None;

    /// <summary>
    /// Whether the library has metadata for this currency, either compiled in or registered through
    /// <see cref="CurrencyRegistry"/>.
    /// </summary>
    /// <remarks>
    /// An unknown currency is still a perfectly usable value: it compares, formats, parses, and
    /// persists correctly. What it lacks is a name, a symbol, and a trustworthy precision — which is
    /// why <see cref="Money.Round(System.MidpointRounding)"/> refuses to round one.
    /// </remarks>
    public bool IsKnown => CurrencyTable.TryGetOrdinal(_packed, out _) || CurrencyRegistry.TryGet(_packed, out _);

    /// <summary>The ISO 4217 numeric code, for example <c>208</c>, or <c>0</c> when unknown.</summary>
    public short NumericCode =>
        CurrencyTable.TryGetOrdinal(_packed, out int ordinal) ? CurrencyTable.GetNumericCode(ordinal)
        : CurrencyRegistry.TryGet(_packed, out CurrencyInfo? info) ? info.NumericCode
        : (short)0;

    /// <summary>The English display name, for example <c>Danish Krone</c>, falling back to the code.</summary>
    public string EnglishName =>
        CurrencyTable.TryGetOrdinal(_packed, out int ordinal) ? CurrencyTable.GetName(ordinal)
        : CurrencyRegistry.TryGet(_packed, out CurrencyInfo? info) ? info.EnglishName
        : Code;

    /// <summary>A display symbol such as <c>kr</c>, falling back to the code.</summary>
    /// <remarks>Symbols are not unique — <c>kr</c> is DKK, NOK, SEK, and ISK. See <see cref="CurrencyInfo.Symbol"/>.</remarks>
    public string Symbol =>
        CurrencyTable.TryGetOrdinal(_packed, out int ordinal) ? CurrencyTable.GetSymbol(ordinal)
        : CurrencyRegistry.TryGet(_packed, out CurrencyInfo? info) ? info.Symbol
        : Code;

    /// <summary>The number of minor-unit digits, for example <c>2</c> for DKK and <c>0</c> for JPY.</summary>
    /// <remarks>Reading this allocates nothing: the values are static data in the assembly.</remarks>
    public byte DecimalDigits =>
        CurrencyTable.TryGetOrdinal(_packed, out int ordinal) ? CurrencyTable.DecimalDigits[ordinal]
        : CurrencyRegistry.TryGet(_packed, out CurrencyInfo? info) ? info.DecimalDigits
        : FallbackDecimalDigits;

    /// <summary>
    /// How many minor units make one major unit: <c>100</c> for most currencies, <c>1000</c> for KWD,
    /// <c>5</c> for MRU and MGA, and <c>0</c> for <c>XXX</c> and <c>XTS</c>, which have no minor unit.
    /// </summary>
    public long MinorUnitsPerMajor =>
        CurrencyTable.TryGetOrdinal(_packed, out int ordinal) ? CurrencyTable.GetMinorUnitsPerMajor(ordinal)
        : CurrencyRegistry.TryGet(_packed, out CurrencyInfo? info) ? info.MinorUnitsPerMajor
        : FallbackMinorUnitsPerMajor;

    /// <summary>Whether this currency has a minor unit. <c>XXX</c> and <c>XTS</c> do not.</summary>
    public bool HasMinorUnit => MinorUnitsPerMajor > 0;

    /// <summary>The digit count used for physical cash, which can be coarser than the accounting precision.</summary>
    public byte CashDecimalDigits =>
        CurrencyTable.TryGetOrdinal(_packed, out int ordinal) ? CurrencyTable.CashDecimalDigits[ordinal]
        : CurrencyRegistry.TryGet(_packed, out CurrencyInfo? info) ? info.CashDecimalDigits
        : DecimalDigits;

    /// <summary>
    /// The cash rounding step in last-place units of <see cref="CashDecimalDigits"/>: <c>5</c> for the
    /// Swiss 0.05 franc, <c>50</c> for the Danish 0.50 krone, <c>1</c> when cash needs no special rounding.
    /// </summary>
    public byte CashRoundingStep =>
        CurrencyTable.TryGetOrdinal(_packed, out int ordinal) ? CurrencyTable.CashRoundingSteps[ordinal]
        : CurrencyRegistry.TryGet(_packed, out CurrencyInfo? info) ? info.CashRoundingStep
        : (byte)1;

    /// <summary>The smallest representable amount, for example <c>0.01</c> for DKK; <c>0</c> when there is no minor unit.</summary>
    public decimal MinorUnit
    {
        get
        {
            long units = MinorUnitsPerMajor;
            return units == 0 ? 0m : 1m / units;
        }
    }

    /// <summary>Full metadata for this currency.</summary>
    /// <remarks>
    /// For an unrecognised currency this synthesises a fallback with <see cref="CurrencyInfo.IsKnown"/>
    /// set to <see langword="false"/> rather than throwing, so loading unfamiliar data never crashes.
    /// The fallback allocates on each call; the scalar properties on <see cref="Currency"/> do not.
    /// </remarks>
    public CurrencyInfo Info => CurrencyMetadata.Get(_packed);

    /// <summary>Every currency known at compile time, ordered by alphabetic code.</summary>
    /// <remarks>
    /// <para>Runtime-registered currencies are not included; see <see cref="CurrencyRegistry.Custom"/>.</para>
    /// <para>
    /// Named <c>Known</c> rather than <c>All</c> because <c>ALL</c> is the Albanian lek, and a property
    /// differing from a currency only by case would be a trap in a case-insensitive language and a
    /// CLS-compliance problem besides.
    /// </para>
    /// </remarks>
    public static ReadOnlySpan<Currency> Known => CurrencyMetadata.AllCurrencies;

    /// <summary>Resolves a currency from its ISO 4217 alphabetic code.</summary>
    /// <param name="code">Three ASCII letters; lower case is accepted and normalised.</param>
    /// <returns>The currency, which need not be one the library recognises.</returns>
    /// <exception cref="ArgumentException"><paramref name="code"/> is not three ASCII letters.</exception>
    public static Currency FromCode(ReadOnlySpan<char> code) =>
        TryFromCode(code, out Currency currency)
            ? currency
            : throw new ArgumentException(
                $"'{code}' is not a valid ISO 4217 alphabetic code; expected three ASCII letters.",
                nameof(code));

    /// <inheritdoc cref="FromCode(ReadOnlySpan{char})" />
    public static Currency FromCode(string code)
    {
        ArgumentNullException.ThrowIfNull(code);
        return FromCode(code.AsSpan());
    }

    /// <summary>Resolves a currency from its ISO 4217 alphabetic code.</summary>
    /// <param name="code">Three ASCII letters; lower case is accepted and normalised.</param>
    /// <param name="currency">The resolved currency, or <see cref="None"/> on failure.</param>
    /// <returns><see langword="true"/> if <paramref name="code"/> is well formed.</returns>
    public static bool TryFromCode(ReadOnlySpan<char> code, out Currency currency)
    {
        bool packed = CurrencyCodec.TryPack(code, out uint value);
        currency = new Currency(value);
        return packed;
    }

    /// <summary>Resolves a currency known at compile time from its ISO 4217 numeric code.</summary>
    /// <param name="numericCode">The ISO numeric code, for example <c>208</c> for DKK.</param>
    /// <param name="currency">The resolved currency, or <see cref="None"/> on failure.</param>
    /// <returns><see langword="true"/> if the numeric code belongs to a known currency.</returns>
    /// <remarks>
    /// Numeric codes are only resolvable for currencies the library knows about, because unlike the
    /// alphabetic code they are not recoverable from the value itself.
    /// </remarks>
    public static bool TryFromNumericCode(short numericCode, out Currency currency) =>
        CurrencyMetadata.TryGetByNumericCode(numericCode, out currency);

    /// <summary>Compares by alphabetic code, ordinally.</summary>
    public int CompareTo(Currency other) => string.CompareOrdinal(Code, other.Code);

    /// <inheritdoc />
    int IComparable.CompareTo(object? obj) => obj switch
    {
        null => 1,
        Currency other => CompareTo(other),
        _ => throw new ArgumentException($"Object must be of type {nameof(Currency)}.", nameof(obj)),
    };

    /// <summary>Orders by alphabetic code.</summary>
    public static bool operator <(Currency left, Currency right) => left.CompareTo(right) < 0;

    /// <summary>Orders by alphabetic code.</summary>
    public static bool operator <=(Currency left, Currency right) => left.CompareTo(right) <= 0;

    /// <summary>Orders by alphabetic code.</summary>
    public static bool operator >(Currency left, Currency right) => left.CompareTo(right) > 0;

    /// <summary>Orders by alphabetic code.</summary>
    public static bool operator >=(Currency left, Currency right) => left.CompareTo(right) >= 0;

    /// <summary>Returns the ISO 4217 alphabetic code.</summary>
    public override string ToString() => Code;

    /// <summary>Formats the currency.</summary>
    /// <param name="format">
    /// <c>G</c> or <c>A</c> (default) for the alphabetic code, <c>N</c> for the zero-padded numeric
    /// code, <c>S</c> for the symbol, <c>L</c> for the English name.
    /// </param>
    /// <param name="formatProvider">Used only by the numeric format.</param>
    /// <exception cref="FormatException"><paramref name="format"/> is not recognised.</exception>
    public string ToString(string? format, IFormatProvider? formatProvider) => Select(format) switch
    {
        CurrencyFormat.Alphabetic => Code,
        CurrencyFormat.Numeric => NumericCode.ToString("D3", formatProvider ?? CultureInfo.InvariantCulture),
        CurrencyFormat.Symbol => Symbol,
        _ => EnglishName,
    };

    /// <inheritdoc cref="ToString(string?, IFormatProvider?)" />
    public bool TryFormat(
        Span<char> destination,
        out int charsWritten,
        ReadOnlySpan<char> format = default,
        IFormatProvider? provider = null)
    {
        switch (Select(format))
        {
            case CurrencyFormat.Alphabetic:
                // The common case writes straight into the caller's buffer with no string in sight.
                if (destination.Length < 3)
                {
                    charsWritten = 0;
                    return false;
                }

                CurrencyCodec.Unpack(_packed, destination);
                charsWritten = 3;
                return true;

            case CurrencyFormat.Numeric:
                return NumericCode.TryFormat(destination, out charsWritten, "D3", provider ?? CultureInfo.InvariantCulture);

            case CurrencyFormat.Symbol:
                return TryCopy(Symbol, destination, out charsWritten);

            default:
                return TryCopy(EnglishName, destination, out charsWritten);
        }
    }

    private static bool TryCopy(string value, Span<char> destination, out int charsWritten)
    {
        if (value.AsSpan().TryCopyTo(destination))
        {
            charsWritten = value.Length;
            return true;
        }

        charsWritten = 0;
        return false;
    }

    /// <inheritdoc cref="ToString(string?, IFormatProvider?)" />
    public bool TryFormat(
        Span<byte> utf8Destination,
        out int bytesWritten,
        ReadOnlySpan<char> format = default,
        IFormatProvider? provider = null)
    {
        switch (Select(format))
        {
            case CurrencyFormat.Alphabetic:
                if (utf8Destination.Length < 3)
                {
                    bytesWritten = 0;
                    return false;
                }

                CurrencyCodec.UnpackUtf8(_packed, utf8Destination);
                bytesWritten = 3;
                return true;

            case CurrencyFormat.Numeric:
                return NumericCode.TryFormat(utf8Destination, out bytesWritten, "D3", provider ?? CultureInfo.InvariantCulture);

            case CurrencyFormat.Symbol:
                return System.Text.Encoding.UTF8.TryGetBytes(Symbol, utf8Destination, out bytesWritten);

            default:
                return System.Text.Encoding.UTF8.TryGetBytes(EnglishName, utf8Destination, out bytesWritten);
        }
    }

    /// <summary>Parses an ISO 4217 alphabetic code.</summary>
    /// <exception cref="FormatException">The input is not three ASCII letters.</exception>
    public static Currency Parse(ReadOnlySpan<char> s, IFormatProvider? provider = null) =>
        TryParse(s, provider, out Currency currency)
            ? currency
            : throw new FormatException($"'{s}' is not a valid ISO 4217 alphabetic code.");

    /// <inheritdoc cref="Parse(ReadOnlySpan{char}, IFormatProvider?)" />
    public static Currency Parse(string s, IFormatProvider? provider = null)
    {
        ArgumentNullException.ThrowIfNull(s);
        return Parse(s.AsSpan(), provider);
    }

    /// <inheritdoc cref="Parse(ReadOnlySpan{char}, IFormatProvider?)" />
    public static Currency Parse(ReadOnlySpan<byte> utf8Text, IFormatProvider? provider = null) =>
        TryParse(utf8Text, provider, out Currency currency)
            ? currency
            : throw new FormatException($"'{System.Text.Encoding.UTF8.GetString(utf8Text)}' is not a valid ISO 4217 alphabetic code.");

    /// <summary>Parses an ISO 4217 alphabetic code, surrounding whitespace permitted.</summary>
    public static bool TryParse(ReadOnlySpan<char> s, IFormatProvider? provider, out Currency result) =>
        TryFromCode(s.Trim(), out result);

    /// <inheritdoc cref="TryParse(ReadOnlySpan{char}, IFormatProvider?, out Currency)" />
    public static bool TryParse([NotNullWhen(true)] string? s, IFormatProvider? provider, out Currency result)
    {
        if (s is null)
        {
            result = None;
            return false;
        }

        return TryParse(s.AsSpan(), provider, out result);
    }

    /// <inheritdoc cref="TryParse(ReadOnlySpan{char}, IFormatProvider?, out Currency)" />
    public static bool TryParse(ReadOnlySpan<byte> utf8Text, IFormatProvider? provider, out Currency result)
    {
        bool packed = CurrencyCodec.TryPackUtf8(Trim(utf8Text), out uint value);
        result = new Currency(value);
        return packed;
    }

    private static ReadOnlySpan<byte> Trim(ReadOnlySpan<byte> value)
    {
        int start = 0;
        int end = value.Length;

        while (start < end && value[start] is (byte)' ' or (byte)'\t' or (byte)'\r' or (byte)'\n')
        {
            start++;
        }

        while (end > start && value[end - 1] is (byte)' ' or (byte)'\t' or (byte)'\r' or (byte)'\n')
        {
            end--;
        }

        return value[start..end];
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static CurrencyFormat Select(ReadOnlySpan<char> format) => format switch
    {
        [] => CurrencyFormat.Alphabetic,
        ['G' or 'g' or 'A' or 'a'] => CurrencyFormat.Alphabetic,
        ['N' or 'n'] => CurrencyFormat.Numeric,
        ['S' or 's'] => CurrencyFormat.Symbol,
        ['L' or 'l'] => CurrencyFormat.Name,
        _ => throw new FormatException($"'{format}' is not a supported Currency format string. Use G, A, N, S, or L."),
    };

    private enum CurrencyFormat
    {
        Alphabetic,
        Numeric,
        Symbol,
        Name,
    }
}
