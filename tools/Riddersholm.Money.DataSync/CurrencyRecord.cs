using System.Text.Json.Serialization;

namespace Riddersholm.Money.DataSync;

/// <summary>The on-disk shape of <c>eng/iso-4217.json</c>.</summary>
internal sealed record CurrencyDataFile
{
    [JsonPropertyName("$schema")]
    public string Schema { get; init; } = "https://github.com/Riddersholm1/Money/blob/main/eng/iso-4217.schema.json";

    /// <summary>Provenance, so a stale data file is obvious in review.</summary>
    public required DataSource Source { get; init; }

    public required IReadOnlyList<CurrencyRecord> Currencies { get; init; }
}

internal sealed record DataSource
{
    public required string Iso { get; init; }
    public required string CldrFractions { get; init; }
    public required string CldrNames { get; init; }
    public required DateOnly RetrievedUtc { get; init; }

    /// <summary>Human-readable summary of the inclusion rules that produced this file.</summary>
    public required string Filter { get; init; }
}

/// <summary>One ISO 4217 currency as consumed by the source generator.</summary>
internal sealed record CurrencyRecord
{
    /// <summary>ISO 4217 alphabetic code, e.g. <c>DKK</c>.</summary>
    public required string Code { get; init; }

    /// <summary>ISO 4217 numeric code, e.g. 208.</summary>
    public required short NumericCode { get; init; }

    /// <summary>English display name, e.g. "Danish Krone".</summary>
    public required string Name { get; init; }

    /// <summary>
    /// Best-effort culture-independent symbol (CLDR narrow symbol for <c>en</c>), falling back to the
    /// code. Localised formatting prefers the caller's <see cref="System.Globalization.NumberFormatInfo"/>.
    /// </summary>
    public required string Symbol { get; init; }

    /// <summary>ISO minor-unit digit count. 0 for currencies with no minor unit.</summary>
    public required byte DecimalDigits { get; init; }

    /// <summary>
    /// How many minor units make one major unit. Usually 10^<see cref="DecimalDigits"/>, but 5 for MRU
    /// and MGA, and <c>0</c> for currencies with no minor unit at all (XTS, XXX).
    /// </summary>
    public required int MinorUnitsPerMajor { get; init; }

    /// <summary>Digit count used for physical cash, which can be coarser than the accounting precision.</summary>
    public required byte CashDecimalDigits { get; init; }

    /// <summary>
    /// Cash rounding step counted in last-place units of <see cref="CashDecimalDigits"/>. CHF is 5
    /// (0.05), DKK is 50 (0.50), and 1 means no special cash rounding.
    /// </summary>
    public required byte CashRoundingStep { get; init; }
}
