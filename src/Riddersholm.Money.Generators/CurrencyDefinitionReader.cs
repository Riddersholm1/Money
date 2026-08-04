using Microsoft.CodeAnalysis;
using Riddersholm.Money.Generators.Json;

namespace Riddersholm.Money.Generators;

/// <summary>Turns the JSON text of a currency definition file into validated definitions.</summary>
internal static class CurrencyDefinitionReader
{
    public static IReadOnlyList<CurrencyDefinition> Read(
        string path,
        string content,
        List<Diagnostic> diagnostics)
    {
        JsonValue? document = JsonParser.Parse(content);

        if (document is null)
        {
            diagnostics.Add(Diagnostic.Create(GeneratorDiagnostics.InvalidJson, Location.None, path));
            return [];
        }

        IReadOnlyList<JsonValue> entries = document["currencies"].AsArray();

        if (entries.Count == 0)
        {
            diagnostics.Add(Diagnostic.Create(GeneratorDiagnostics.MissingCurrencies, Location.None, path));
            return [];
        }

        List<CurrencyDefinition> definitions = new(entries.Count);
        HashSet<string> seen = [];

        foreach (JsonValue entry in entries)
        {
            string code = entry["code"].AsString() ?? string.Empty;

            if (!CurrencyCodec.TryPack(code, out uint packed))
            {
                diagnostics.Add(Diagnostic.Create(GeneratorDiagnostics.InvalidCode, Location.None, code, path));
                continue;
            }

            if (!seen.Add(code))
            {
                diagnostics.Add(Diagnostic.Create(GeneratorDiagnostics.DuplicateCode, Location.None, code, path));
                continue;
            }

            long digits = entry["decimalDigits"].AsInt64();

            // 28 is decimal's own limit, not ISO's. ISO currencies stop at 4, but registered
            // currencies routinely need more — Bitcoin has 8 decimal places and Ether has 18 — and
            // the ISO ceiling is enforced by the data pipeline that produces eng/iso-4217.json.
            if (digits is < 0 or > 28)
            {
                diagnostics.Add(Diagnostic.Create(GeneratorDiagnostics.InvalidPrecision, Location.None, code, digits));
                continue;
            }

            // A missing minorUnitsPerMajor means "the usual power of ten", which keeps hand-written
            // definition files terse: only MRU/MGA and the no-minor-unit codes need to say anything.
            JsonValue minorUnits = entry["minorUnitsPerMajor"];
            long minorUnitsPerMajor = minorUnits.IsNull ? Pow10((int)digits) : minorUnits.AsInt64();

            if (minorUnitsPerMajor is < 0 or > 1_000_000_000_000_000_000L)
            {
                diagnostics.Add(Diagnostic.Create(GeneratorDiagnostics.InvalidPrecision, Location.None, code, digits));
                continue;
            }

            byte cashDigits = (byte)entry["cashDecimalDigits"].AsInt64(digits);
            byte cashStep = (byte)entry["cashRoundingStep"].AsInt64(1);

            definitions.Add(new CurrencyDefinition(
                Code: code,
                Packed: packed,
                NumericCode: (short)entry["numericCode"].AsInt64(),
                Name: entry["name"].AsString() ?? code,
                Symbol: entry["symbol"].AsString() ?? code,
                DecimalDigits: (byte)digits,
                MinorUnitsPerMajor: minorUnitsPerMajor,
                CashDecimalDigits: cashDigits,
                CashRoundingStep: cashStep == 0 ? (byte)1 : cashStep));
        }

        // Deterministic output: identical inputs must always produce byte-identical source.
        definitions.Sort(static (a, b) => string.CompareOrdinal(a.Code, b.Code));
        return definitions;
    }

    private static long Pow10(int exponent)
    {
        long result = 1;
        for (int i = 0; i < exponent && result <= 100_000_000_000_000_000L; i++)
        {
            result *= 10;
        }

        return result;
    }
}
