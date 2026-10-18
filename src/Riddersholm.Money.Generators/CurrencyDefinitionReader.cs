using Microsoft.CodeAnalysis;
using Riddersholm.Money.Generators.Json;

namespace Riddersholm.Money.Generators;

/// <summary>Turns the JSON text of a currency definition file into validated definitions.</summary>
/// <remarks>
/// Every value is checked against the range the runtime accepts, and a value that is present but wrong
/// is an error rather than a default. The reader this replaced cast numbers straight to <c>byte</c> and
/// <c>short</c>, so a cash step of 256 became 0 and then quietly 1, a numeric code of 70000 wrapped to
/// 4464, and a missing <c>decimalDigits</c> produced a currency with no decimals at all. Each of those
/// is a currency that builds cleanly and rounds money wrongly.
/// </remarks>
internal static class CurrencyDefinitionReader
{
    /// <summary>The largest precision the runtime supports: decimal's own limit.</summary>
    private const long MaximumDecimalDigits = 28;

    /// <summary>The largest minor-unit divisor that fits the runtime's <see cref="long"/>.</summary>
    private const long MaximumMinorUnitsPerMajor = 1_000_000_000_000_000_000L;

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

            if (TryReadDefinition(entry, code, packed, path, diagnostics) is { } definition)
            {
                definitions.Add(definition);
            }
        }

        // Deterministic output: identical inputs must always produce byte-identical source.
        definitions.Sort(static (a, b) => string.CompareOrdinal(a.Code, b.Code));
        return definitions;
    }

    private static CurrencyDefinition? TryReadDefinition(
        JsonValue entry,
        string code,
        uint packed,
        string path,
        List<Diagnostic> diagnostics)
    {
        // Required, because there is no safe default: assuming two would make JPY gain decimals, and
        // assuming zero would make every other currency lose them.
        JsonValue digitsValue = entry["decimalDigits"];

        if (!digitsValue.TryGetInt64(out long digits))
        {
            Report(diagnostics, code, path, digitsValue.IsNull
                ? "'decimalDigits' is required"
                : "'decimalDigits' must be a whole number");
            return null;
        }

        // 28 is decimal's own limit, not ISO's. ISO currencies stop at 4, but registered currencies
        // routinely need more — Bitcoin has 8 decimal places and Ether has 18 — and the ISO ceiling is
        // enforced by the data pipeline that produces eng/iso-4217.json.
        if (digits is < 0 or > MaximumDecimalDigits)
        {
            diagnostics.Add(Diagnostic.Create(GeneratorDiagnostics.InvalidPrecision, Location.None, code, digits));
            return null;
        }

        if (!TryReadMinorUnits(entry, code, digits, path, diagnostics, out long minorUnitsPerMajor)
            || !TryReadInteger(entry, "numericCode", fallback: 0, minimum: 0, maximum: 999, code, path, diagnostics, out long numericCode)
            || !TryReadInteger(entry, "cashDecimalDigits", fallback: digits, minimum: 0, maximum: MaximumDecimalDigits, code, path, diagnostics, out long cashDigits)
            // Zero is CLDR's way of saying "no special cash rounding", which is a step of one.
            || !TryReadInteger(entry, "cashRoundingStep", fallback: 1, minimum: 0, maximum: byte.MaxValue, code, path, diagnostics, out long cashStep)
            || !TryReadText(entry, "name", code, path, diagnostics, out string? name)
            || !TryReadText(entry, "symbol", code, path, diagnostics, out string? symbol)
            || !TryReadWithdrawal(entry, code, path, diagnostics, out string? withdrawn, out string? replacedBy))
        {
            return null;
        }

        return new CurrencyDefinition(
            Code: code,
            Packed: packed,
            NumericCode: (short)numericCode,
            Name: name ?? code,
            Symbol: symbol ?? code,
            DecimalDigits: (byte)digits,
            MinorUnitsPerMajor: minorUnitsPerMajor,
            CashDecimalDigits: (byte)cashDigits,
            CashRoundingStep: cashStep == 0 ? (byte)1 : (byte)cashStep,
            Withdrawn: withdrawn,
            ReplacedBy: replacedBy);
    }

    private static bool TryReadMinorUnits(
        JsonValue entry,
        string code,
        long digits,
        string path,
        List<Diagnostic> diagnostics,
        out long minorUnitsPerMajor)
    {
        // A missing minorUnitsPerMajor means "the usual power of ten", which keeps hand-written
        // definition files terse: only MRU/MGA and the no-minor-unit codes need to say anything.
        if (entry["minorUnitsPerMajor"].IsNull)
        {
            if (digits > 18)
            {
                // 10^19 does not fit the runtime's long, and quietly substituting 10^18 would round
                // this currency to 18 places while it claims more.
                minorUnitsPerMajor = 0;
                Report(diagnostics, code, path, "'minorUnitsPerMajor' must be given explicitly above 18 decimal digits, because 10^decimalDigits does not fit");
                return false;
            }

            minorUnitsPerMajor = Pow10(digits);
            return true;
        }

        if (!TryReadInteger(entry, "minorUnitsPerMajor", fallback: 0, minimum: 0, maximum: MaximumMinorUnitsPerMajor, code, path, diagnostics, out minorUnitsPerMajor))
        {
            return false;
        }

        // The runtime refuses this combination when the currency registers, which for a consumer's file
        // means a module initializer throwing at startup. A build error is the better place to learn it.
        if (minorUnitsPerMajor > 0 && !DividesPowerOfTen(minorUnitsPerMajor, digits))
        {
            Report(diagnostics, code, path, $"'minorUnitsPerMajor' {minorUnitsPerMajor} cannot be expressed in {digits} decimal places; it must divide 10^{digits}");
            return false;
        }

        return true;
    }

    private static bool TryReadInteger(
        JsonValue entry,
        string property,
        long fallback,
        long minimum,
        long maximum,
        string code,
        string path,
        List<Diagnostic> diagnostics,
        out long value)
    {
        JsonValue raw = entry[property];

        if (raw.IsNull)
        {
            value = fallback;
            return true;
        }

        if (!raw.TryGetInt64(out value))
        {
            Report(diagnostics, code, path, $"'{property}' must be a whole number");
            return false;
        }

        if (value < minimum || value > maximum)
        {
            Report(diagnostics, code, path, $"'{property}' is {value}, outside the supported range {minimum} to {maximum}");
            return false;
        }

        return true;
    }

    private static bool TryReadText(
        JsonValue entry,
        string property,
        string code,
        string path,
        List<Diagnostic> diagnostics,
        out string? value)
    {
        JsonValue raw = entry[property];
        value = raw.AsString();

        if (raw.IsNull || value is not null)
        {
            return true;
        }

        Report(diagnostics, code, path, $"'{property}' must be a string");
        return false;
    }

    /// <summary>
    /// Reads the optional withdrawal date, and the currency that replaced it.
    /// </summary>
    /// <remarks>
    /// A withdrawn currency stays in the table so that historical amounts keep their metadata, and the
    /// emitters mark its member <c>[Obsolete]</c>. Deleting it instead would remove a public member —
    /// a breaking change for every consumer that names it — and leave years of stored amounts with no
    /// known precision to round them by.
    /// </remarks>
    private static bool TryReadWithdrawal(
        JsonValue entry,
        string code,
        string path,
        List<Diagnostic> diagnostics,
        out string? withdrawn,
        out string? replacedBy)
    {
        withdrawn = null;
        replacedBy = null;

        JsonValue withdrawnValue = entry["withdrawn"];

        if (!withdrawnValue.IsNull)
        {
            withdrawn = withdrawnValue.AsString();

            if (withdrawn is null || !IsIsoMonthOrDate(withdrawn))
            {
                Report(diagnostics, code, path, "'withdrawn' must be a date written as YYYY-MM or YYYY-MM-DD");
                return false;
            }
        }

        JsonValue replacedByValue = entry["replacedBy"];

        if (!replacedByValue.IsNull)
        {
            replacedBy = replacedByValue.AsString();

            if (replacedBy is null || !CurrencyCodec.TryPack(replacedBy, out _))
            {
                Report(diagnostics, code, path, "'replacedBy' must be an ISO 4217 alphabetic code");
                return false;
            }

            if (withdrawn is null)
            {
                Report(diagnostics, code, path, "'replacedBy' is only meaningful together with 'withdrawn'");
                return false;
            }
        }

        return true;
    }

    private static bool IsIsoMonthOrDate(string value)
    {
        if (value.Length is not (7 or 10))
        {
            return false;
        }

        for (int i = 0; i < value.Length; i++)
        {
            bool separator = i is 4 or 7;

            if (separator ? value[i] != '-' : value[i] is < '0' or > '9')
            {
                return false;
            }
        }

        int month = ((value[5] - '0') * 10) + (value[6] - '0');
        int day = value.Length == 10 ? ((value[8] - '0') * 10) + (value[9] - '0') : 1;

        return month is >= 1 and <= 12 && day is >= 1 and <= 31;
    }

    private static void Report(List<Diagnostic> diagnostics, string code, string path, string problem) =>
        diagnostics.Add(Diagnostic.Create(GeneratorDiagnostics.InvalidProperty, Location.None, code, path, problem));

    /// <summary>Whether <paramref name="divisor"/> divides <c>10^</c><paramref name="exponent"/>.</summary>
    /// <remarks>
    /// A divisor of a power of ten has no prime factors but two and five, and no more of either than the
    /// exponent. Testing that directly avoids computing 10^28, which no integer type here can hold.
    /// </remarks>
    private static bool DividesPowerOfTen(long divisor, long exponent)
    {
        int twos = 0;
        int fives = 0;

        while (divisor % 2 == 0)
        {
            divisor /= 2;
            twos++;
        }

        while (divisor % 5 == 0)
        {
            divisor /= 5;
            fives++;
        }

        return divisor == 1 && twos <= exponent && fives <= exponent;
    }

    private static long Pow10(long exponent)
    {
        long result = 1;
        for (long i = 0; i < exponent; i++)
        {
            result *= 10;
        }

        return result;
    }
}
