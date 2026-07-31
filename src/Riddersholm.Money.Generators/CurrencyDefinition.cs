namespace Riddersholm.Money.Generators;

/// <summary>One currency read from an ISO 4217 definition file.</summary>
internal sealed record CurrencyDefinition(
    string Code,
    uint Packed,
    short NumericCode,
    string Name,
    string Symbol,
    byte DecimalDigits,
    long MinorUnitsPerMajor,
    byte CashDecimalDigits,
    byte CashRoundingStep);
