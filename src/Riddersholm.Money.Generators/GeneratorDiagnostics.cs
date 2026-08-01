using Microsoft.CodeAnalysis;

namespace Riddersholm.Money.Generators;

/// <summary>
/// Diagnostics raised while reading a currency definition file. Bad currency data is a correctness
/// problem, so every one of these is an error rather than a warning — silently skipping a malformed
/// entry would produce a library that is quietly missing a currency.
/// </summary>
internal static class GeneratorDiagnostics
{
    private const string Category = "Riddersholm.Money.Generators";

    public static readonly DiagnosticDescriptor InvalidJson = new(
        id: "RMG001",
        title: "Currency definition file is not valid JSON",
        messageFormat: "'{0}' could not be parsed as JSON",
        category: Category,
        defaultSeverity: DiagnosticSeverity.Error,
        isEnabledByDefault: true);

    public static readonly DiagnosticDescriptor MissingCurrencies = new(
        id: "RMG002",
        title: "Currency definition file has no currencies",
        messageFormat: "'{0}' does not contain a non-empty 'currencies' array",
        category: Category,
        defaultSeverity: DiagnosticSeverity.Error,
        isEnabledByDefault: true);

    public static readonly DiagnosticDescriptor InvalidCode = new(
        id: "RMG003",
        title: "Currency code is not a valid ISO 4217 alphabetic code",
        messageFormat: "'{0}' in '{1}' is not three uppercase ASCII letters",
        category: Category,
        defaultSeverity: DiagnosticSeverity.Error,
        isEnabledByDefault: true);

    public static readonly DiagnosticDescriptor DuplicateCode = new(
        id: "RMG004",
        title: "Duplicate currency code",
        messageFormat: "'{0}' is defined more than once in '{1}'",
        category: Category,
        defaultSeverity: DiagnosticSeverity.Error,
        isEnabledByDefault: true);

    public static readonly DiagnosticDescriptor InvalidPrecision = new(
        id: "RMG005",
        title: "Currency precision is implausible",
        messageFormat: "'{0}' declares {1} decimal digits; ISO 4217 currencies have at most 4",
        category: Category,
        defaultSeverity: DiagnosticSeverity.Error,
        isEnabledByDefault: true);
}
