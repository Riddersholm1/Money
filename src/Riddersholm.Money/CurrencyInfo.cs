namespace Riddersholm.Money;

/// <summary>
/// Descriptive metadata about a currency: its name, symbol, precision, and cash rounding rules.
/// </summary>
/// <remarks>
/// <para>
/// This is deliberately <em>not</em> part of <see cref="Currency"/>. A currency is an identity — four
/// bytes that compare with a single integer comparison — while its metadata is a handful of strings.
/// Storing the metadata inside the value type would make every <see cref="Money"/> large to copy and
/// every comparison a string comparison, for information that most code never reads.
/// </para>
/// <para>
/// The split mirrors how .NET separates a culture <em>name</em> from <see cref="System.Globalization.CultureInfo"/>.
/// For convenience <see cref="Currency"/> forwards the common properties, so <c>Currency.DKK.Symbol</c>
/// reads naturally without the metadata living in the struct.
/// </para>
/// </remarks>
public sealed class CurrencyInfo
{
    /// <summary>Creates metadata for a currency, typically from generated code.</summary>
    /// <param name="code">The ISO 4217 alphabetic code — three uppercase ASCII letters.</param>
    /// <param name="numericCode">The ISO 4217 numeric code, or <c>0</c> when the currency has none.</param>
    /// <param name="englishName">The English display name, for example <c>Danish Krone</c>.</param>
    /// <param name="symbol">A display symbol such as <c>kr</c>; use <paramref name="code"/> when there is none.</param>
    /// <param name="decimalDigits">
    /// The number of minor-unit digits, <c>0</c> to <see cref="MaximumDecimalDigits"/>. ISO currencies
    /// use at most 4; the wider range exists for registered currencies such as Bitcoin, which has 8.
    /// </param>
    /// <param name="minorUnitsPerMajor">
    /// How many minor units make one major unit — usually a power of ten, but <c>5</c> for MRU and MGA,
    /// and <c>0</c> for currencies with no minor unit at all.
    /// </param>
    /// <param name="cashDecimalDigits">The digit count used for physical cash, which may be coarser.</param>
    /// <param name="cashRoundingStep">
    /// The cash rounding step counted in last-place units of <paramref name="cashDecimalDigits"/>; <c>1</c>
    /// means no special cash rounding, <c>5</c> gives the Swiss 0.05 franc, <c>50</c> the Danish 0.50 krone.
    /// </param>
    /// <exception cref="ArgumentException"><paramref name="code"/> is not a valid ISO 4217 alphabetic code.</exception>
    /// <exception cref="ArgumentOutOfRangeException">A precision argument is outside its supported range.</exception>
    public CurrencyInfo(
        string code,
        short numericCode,
        string englishName,
        string symbol,
        byte decimalDigits,
        long minorUnitsPerMajor,
        byte cashDecimalDigits,
        byte cashRoundingStep)
    {
        ArgumentNullException.ThrowIfNull(code);
        ArgumentNullException.ThrowIfNull(englishName);
        ArgumentNullException.ThrowIfNull(symbol);

        if (!CurrencyCodec.TryPack(code, out uint packed))
        {
            throw new ArgumentException(
                $"'{code}' is not a valid ISO 4217 alphabetic code; expected three ASCII letters.",
                nameof(code));
        }

        ArgumentOutOfRangeException.ThrowIfGreaterThan(decimalDigits, MaximumDecimalDigits, nameof(decimalDigits));
        ArgumentOutOfRangeException.ThrowIfNegative(minorUnitsPerMajor, nameof(minorUnitsPerMajor));
        ArgumentOutOfRangeException.ThrowIfGreaterThan(minorUnitsPerMajor, MaximumMinorUnitsPerMajor, nameof(minorUnitsPerMajor));
        ArgumentOutOfRangeException.ThrowIfGreaterThan(cashDecimalDigits, MaximumDecimalDigits, nameof(cashDecimalDigits));
        ArgumentOutOfRangeException.ThrowIfZero(cashRoundingStep, nameof(cashRoundingStep));

        // The minor unit has to be expressible in the declared number of decimal places, or rounding
        // would snap to an increment the currency cannot represent. 100/5 is fine — that is MRU's
        // khoum — but 2 digits with 10^18 units is a mis-registration, and silently accepting it
        // produces amounts that look canonical and are not.
        //
        // Checked in decimal across the whole 0..28 range. An earlier version stopped at 18 because it
        // computed the power in a long, which left exactly the registrations that also confused
        // Money.Round unvalidated.
        if (minorUnitsPerMajor > 0 && Pow10(decimalDigits) % minorUnitsPerMajor != 0m)
        {
            throw new ArgumentException(
                $"'{code}' declares {minorUnitsPerMajor} minor units per major unit, which cannot be "
              + $"expressed in {decimalDigits} decimal places. The divisor must divide 10^{decimalDigits}.",
                nameof(minorUnitsPerMajor));
        }

        Currency = new Currency(packed);
        // Stored rather than delegated to Currency.Code: that property consults this object, so
        // forwarding here would recurse.
        Code = CurrencyCodec.Decode(packed);
        NumericCode = numericCode;
        EnglishName = englishName;
        Symbol = symbol;
        DecimalDigits = decimalDigits;
        MinorUnitsPerMajor = minorUnitsPerMajor;
        CashDecimalDigits = cashDecimalDigits;
        CashRoundingStep = cashRoundingStep;
    }

    /// <summary>
    /// The largest precision this type supports, which is <see cref="decimal"/>'s own limit.
    /// </summary>
    /// <remarks>
    /// ISO 4217 currencies use at most <see cref="MaximumIsoDecimalDigits"/>. The wider range exists
    /// because registered currencies need it — Bitcoin has 8 decimal places and Ether has 18 — and
    /// capping the type at the ISO limit would make those unrepresentable for no benefit.
    /// </remarks>
    public const byte MaximumDecimalDigits = 28;

    /// <summary>The largest precision any ISO 4217 currency declares.</summary>
    public const byte MaximumIsoDecimalDigits = 4;

    /// <summary>The largest minor-unit divisor that fits the backing <see cref="long"/>.</summary>
    public const long MaximumMinorUnitsPerMajor = 1_000_000_000_000_000_000L;

    /// <summary>The currency this metadata describes.</summary>
    public Currency Currency { get; }

    /// <summary>The ISO 4217 alphabetic code, for example <c>DKK</c>.</summary>
    public string Code { get; }

    /// <summary>The ISO 4217 numeric code, for example <c>208</c>, or <c>0</c> when there is none.</summary>
    public short NumericCode { get; }

    /// <summary>The English display name, for example <c>Danish Krone</c>.</summary>
    public string EnglishName { get; }

    /// <summary>
    /// A display symbol such as <c>kr</c>, <c>$</c>, or <c>¥</c>, falling back to the code when the
    /// currency has no distinct symbol.
    /// </summary>
    /// <remarks>
    /// Symbols are <b>not</b> unique: <c>kr</c> is DKK, NOK, SEK, and ISK. Never infer a currency from
    /// a symbol without knowing the culture it was written in.
    /// </remarks>
    public string Symbol { get; }

    /// <summary>The number of minor-unit digits, for example <c>2</c> for DKK and <c>0</c> for JPY.</summary>
    public byte DecimalDigits { get; }

    /// <summary>
    /// How many minor units make one major unit: <c>100</c> for most currencies, <c>1000</c> for KWD,
    /// <c>5</c> for MRU and MGA, and <c>0</c> for currencies with no minor unit.
    /// </summary>
    /// <remarks>
    /// This is not always a power of ten. The Mauritanian khoum and Malagasy iraimbilanja are one
    /// <em>fifth</em> of the major unit, so valid MRU amounts step by <c>0.2</c> rather than <c>0.01</c>,
    /// even though ISO records two decimal digits for it.
    /// </remarks>
    public long MinorUnitsPerMajor { get; }

    /// <summary>Whether this currency has a minor unit at all. <c>XXX</c> and <c>XTS</c> do not.</summary>
    public bool HasMinorUnit => MinorUnitsPerMajor > 0;

    /// <summary>The digit count used for physical cash, which can be coarser than the accounting precision.</summary>
    public byte CashDecimalDigits { get; }

    /// <summary>
    /// The cash rounding step in last-place units of <see cref="CashDecimalDigits"/>: <c>5</c> for the
    /// Swiss 0.05 franc, <c>50</c> for the Danish 0.50 krone, <c>1</c> when cash needs no special rounding.
    /// </summary>
    public byte CashRoundingStep { get; }

    /// <summary>
    /// Whether this currency was known at compile time. <see langword="false"/> means the metadata is a
    /// documented fallback for a code the library has not seen — see <see cref="Currency.IsKnown"/>.
    /// </summary>
    public bool IsKnown { get; internal init; } = true;

    /// <summary>The smallest representable amount, for example <c>0.01</c> for DKK; <c>0</c> when there is no minor unit.</summary>
    public decimal MinorUnit => MinorUnitsPerMajor == 0 ? 0m : 1m / MinorUnitsPerMajor;

    /// <summary>Returns the ISO 4217 alphabetic code.</summary>
    public override string ToString() => Code;

    /// <summary>Ten to the power of <paramref name="exponent"/>, in decimal so the full 0..28 range fits.</summary>
    private static decimal Pow10(int exponent)
    {
        decimal result = 1m;

        for (int i = 0; i < exponent; i++)
        {
            result *= 10m;
        }

        return result;
    }
}
