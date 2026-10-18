using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization;
using Riddersholm.Money.DataSync;

// Refreshes eng/iso-4217.json from the ISO 4217 register and the Unicode CLDR.
//
// Run manually when ISO publishes an amendment; the output is committed so that builds are
// offline-reproducible and currency changes show up as a reviewable data diff rather than a
// regenerated blob of C#.
//
//   dotnet run --project tools/Riddersholm.Money.DataSync            rewrite the file
//   dotnet run --project tools/Riddersholm.Money.DataSync -- --check  report only; exit 2 on change
//
// --check is what the scheduled currency-data workflow runs. It never writes, prints a summary of what
// changed, and exits 0 when the shipped data is current, 2 when it is not, and anything else on error.

const string IsoUrl = "https://raw.githubusercontent.com/datasets/currency-codes/main/data/codes-all.csv";
const string CldrFractionsUrl = "https://raw.githubusercontent.com/unicode-org/cldr-json/main/cldr-json/cldr-core/supplemental/currencyData.json";
const string CldrNamesUrl = "https://raw.githubusercontent.com/unicode-org/cldr-json/main/cldr-json/cldr-numbers-full/main/en/currencies.json";
const int ChangesFound = 2;

bool checkOnly = args.Contains("--check", StringComparer.Ordinal);

// Currencies whose ISO MinorUnit is numeric but which are units of account rather than money.
// The MinorUnit == "-" test catches metals, bond units, SDR and friends; these do not have it.
string[] deny = ["XAD"];

// ISO marks these with MinorUnit "-" (so the filter would drop them), but they are structurally
// required: XXX backs Currency.None / default(Currency), and XTS exists for test doubles.
string[] allow = ["XTS", "XXX"];

// ISO records MinorUnit 2 for both, but the khoum and the iraimbilanja are one fifth of the major
// unit, not one hundredth: valid MRU amounts step by 0.2. See docs/currency-data.md.
Dictionary<string, int> minorUnitOverrides = new(StringComparer.Ordinal)
{
    ["MRU"] = 5,
    ["MGA"] = 5
};

JsonSerializerOptions options = new(JsonSerializerDefaults.Web)
{
    WriteIndented = true,
    DefaultIgnoreCondition = JsonIgnoreCondition.Never,
    // Write '₪' rather than '\u20AA'. This file is reviewed by humans in diffs, and it is data —
    // never interpolated into HTML — so the relaxed encoder carries no injection risk here.
    Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping
};

string repoRoot = FindRepositoryRoot();
string outputPath = Path.Combine(repoRoot, "eng", "iso-4217.json");

// The file as it stands is an input too: a currency that has ever shipped stays in it.
CurrencyDataFile? previous = File.Exists(outputPath)
    ? JsonSerializer.Deserialize<CurrencyDataFile>(await File.ReadAllTextAsync(outputPath).ConfigureAwait(false), options)
    : null;

using HttpClient http = new();
http.Timeout = TimeSpan.FromMinutes(2);

Console.WriteLine("Downloading ISO 4217 register...");
string csv = await http.GetStringAsync(new Uri(IsoUrl)).ConfigureAwait(false);

Console.WriteLine("Downloading CLDR fraction data...");
using var fractionsDoc = JsonDocument.Parse(await http.GetStringAsync(new Uri(CldrFractionsUrl)).ConfigureAwait(false));

Console.WriteLine("Downloading CLDR currency names...");
using var namesDoc = JsonDocument.Parse(await http.GetStringAsync(new Uri(CldrNamesUrl)).ConfigureAwait(false));

JsonElement fractions = fractionsDoc.RootElement
    .GetProperty("supplemental").GetProperty("currencyData").GetProperty("fractions");

JsonElement names = namesDoc.RootElement
    .GetProperty("main").GetProperty("en").GetProperty("numbers").GetProperty("currencies");

JsonElement defaultFraction = fractions.GetProperty("DEFAULT");
byte defaultDigits = ReadByte(defaultFraction, "_digits", 2);

// Collapse the country-per-row ISO list into one row per currency, keeping the historic rows aside:
// they say when a currency we already ship was withdrawn, and which entity used it.
Dictionary<string, Dictionary<string, string>> byCode = new(StringComparer.Ordinal);
Dictionary<string, List<Dictionary<string, string>>> withdrawals = new(StringComparer.Ordinal);
Dictionary<string, HashSet<string>> activeCodesByEntity = new(StringComparer.Ordinal);

foreach (Dictionary<string, string> row in Csv.Parse(csv))
{
    string code = row["AlphabeticCode"];
    if (code.Length != 3)
    {
        continue;
    }

    // Historic currencies carry a withdrawal date; we ship active money only.
    if (!string.IsNullOrWhiteSpace(row["WithdrawalDate"]))
    {
        if (!withdrawals.TryGetValue(code, out List<Dictionary<string, string>>? rows))
        {
            withdrawals[code] = rows = [];
        }

        rows.Add(row);
        continue;
    }

    byCode.TryAdd(code, row);

    if (!activeCodesByEntity.TryGetValue(row["Entity"], out HashSet<string>? entityCodes))
    {
        activeCodesByEntity[row["Entity"]] = entityCodes = new HashSet<string>(StringComparer.Ordinal);
    }

    entityCodes.Add(code);
}

List<CurrencyRecord> currencies = [];

foreach ((string code, Dictionary<string, string> row) in byCode)
{
    bool isAllowListed = allow.Contains(code, StringComparer.Ordinal);
    bool hasMinorUnit = byte.TryParse(row["MinorUnit"], CultureInfo.InvariantCulture, out byte isoDigits);

    if (!isAllowListed && (!hasMinorUnit || deny.Contains(code, StringComparer.Ordinal)))
    {
        continue;
    }

    byte digits = isAllowListed && !hasMinorUnit ? (byte)0 : isoDigits;

    int minorUnitsPerMajor = isAllowListed && !hasMinorUnit
        ? 0 // XTS/XXX have no minor unit at all; rounding to one is meaningless.
        : minorUnitOverrides.TryGetValue(code, out int over) ? over : Pow10(digits);

    JsonElement fraction = fractions.TryGetProperty(code, out JsonElement f) ? f : defaultFraction;
    byte cashDigits = ReadByte(fraction, "_cashDigits", ReadByte(fraction, "_digits", defaultDigits));
    byte cashStep = ReadByte(fraction, "_cashRounding", 0);

    if (cashStep == 0)
    {
        cashStep = 1;
    }

    // CLDR describes cash precision on the assumption that the minor unit is a power of ten, which is
    // where the minorUnitOverrides above break the assumption. For MRU it yields a cash increment of
    // 0.01 while the khoum is 0.2, so cash rounding would produce amounts nobody can hold. Cash can be
    // coarser than the currency's own unit but never finer, so the minor unit is the floor.
    if (minorUnitsPerMajor > 0)
    {
        (cashDigits, cashStep) = CoerceCashIncrement(digits, minorUnitsPerMajor, cashDigits, cashStep);
    }

    currencies.Add(new CurrencyRecord
    {
        Code = code,
        NumericCode = short.Parse(row["NumericCode"], CultureInfo.InvariantCulture),
        Name = ReadName(names, code) ?? Capitalise(row["Currency"]),
        Symbol = ReadSymbol(names, code) ?? code,
        DecimalDigits = digits,
        MinorUnitsPerMajor = minorUnitsPerMajor,
        // A currency with no minor unit cannot have cash precision finer than whole units either.
        CashDecimalDigits = minorUnitsPerMajor == 0 ? (byte)0 : cashDigits,
        CashRoundingStep = cashStep
    });
}

// A currency that has been shipped is never dropped. Deleting Currency.BGN when Bulgaria adopted the
// euro would have broken every consumer that names it, and left years of stored lev amounts with no
// precision to round by. It stays, marked withdrawn, and the generator makes the member [Obsolete].
HashSet<string> active = currencies.Select(static c => c.Code).ToHashSet(StringComparer.Ordinal);

foreach (CurrencyRecord shipped in previous?.Currencies ?? [])
{
    if (active.Contains(shipped.Code))
    {
        continue;
    }

    (string withdrawn, string? replacedBy) = shipped.Withdrawn is not null
        ? (shipped.Withdrawn, shipped.ReplacedBy)
        : FindWithdrawal(shipped.Code, withdrawals, activeCodesByEntity, active);

    currencies.Add(shipped with { Withdrawn = withdrawn, ReplacedBy = replacedBy });
}

currencies.Sort(static (a, b) => string.CompareOrdinal(a.Code, b.Code));

Validate(currencies);

List<string> changes = Describe(previous?.Currencies ?? [], currencies);

if (changes.Count == 0)
{
    Console.WriteLine($"No currency changes: the {currencies.Count} shipped currencies match ISO 4217 and CLDR.");
}
else
{
    Console.WriteLine($"{changes.Count} currency change(s) against the committed eng/iso-4217.json:");

    foreach (string change in changes)
    {
        Console.WriteLine("  " + change);
    }
}

if (checkOnly)
{
    return changes.Count == 0 ? 0 : ChangesFound;
}

CurrencyDataFile file = new()
{
    Source = new DataSource
    {
        Iso = IsoUrl,
        CldrFractions = CldrFractionsUrl,
        CldrNames = CldrNamesUrl,
        RetrievedUtc = DateOnly.FromDateTime(DateTime.UtcNow),
        Filter = "Active ISO 4217 codes with a numeric minor unit (excludes metals, bond units, SDR and other funds), minus XAD (unit of account), plus XTS and XXX. Currencies withdrawn after they shipped are kept and marked withdrawn."
    },
    Currencies = currencies
};

Directory.CreateDirectory(Path.GetDirectoryName(outputPath)!);
await File.WriteAllTextAsync(outputPath, JsonSerializer.Serialize(file, options) + Environment.NewLine).ConfigureAwait(false);

Console.WriteLine($"Wrote {currencies.Count} currencies to {outputPath}");
return 0;

// When the register withdrew a currency, and — when the entities that used it now all use one
// currency we ship — what replaced it.
static (string Withdrawn, string? ReplacedBy) FindWithdrawal(
    string code,
    Dictionary<string, List<Dictionary<string, string>>> withdrawals,
    Dictionary<string, HashSet<string>> activeCodesByEntity,
    HashSet<string> shipped)
{
    if (!withdrawals.TryGetValue(code, out List<Dictionary<string, string>>? rows))
    {
        // Gone from the register altogether, which ISO does not do; date it today and say so.
        string today = DateTime.UtcNow.ToString("yyyy-MM", CultureInfo.InvariantCulture);
        Console.WriteLine($"warning: {code} is no longer in the ISO register at all; recording it as withdrawn {today}.");
        return (today, null);
    }

    // A code can have been withdrawn more than once (HRK was, in 2015 and 2023). The latest
    // well-formed YYYY-MM date is the one that matters.
    List<Dictionary<string, string>> latest = rows
        .Where(static r => IsYearMonth(r["WithdrawalDate"]))
        .GroupBy(static r => r["WithdrawalDate"], StringComparer.Ordinal)
        .OrderByDescending(static g => g.Key, StringComparer.Ordinal)
        .FirstOrDefault()?
        .ToList() ?? rows;

    string date = IsYearMonth(latest[0]["WithdrawalDate"])
        ? latest[0]["WithdrawalDate"]
        : DateTime.UtcNow.ToString("yyyy-MM", CultureInfo.InvariantCulture);

    HashSet<string>? candidates = null;

    foreach (Dictionary<string, string> row in latest)
    {
        HashSet<string> successors = activeCodesByEntity.TryGetValue(row["Entity"], out HashSet<string>? codes)
            ? codes.Where(shipped.Contains).ToHashSet(StringComparer.Ordinal)
            : [];

        if (candidates is null)
        {
            candidates = successors;
        }
        else
        {
            candidates.IntersectWith(successors);
        }
    }

    // Ambiguous when an entity uses several codes — Switzerland's CHF, CHE and CHW — so say nothing.
    return (date, candidates is { Count: 1 } ? candidates.First() : null);
}

static bool IsYearMonth(string value) =>
    value.Length == 7 && value[4] == '-' && value.Remove(4, 1).All(char.IsAsciiDigit);

// A reviewable, one-line-per-change summary of what this run would alter.
static List<string> Describe(IReadOnlyList<CurrencyRecord> before, List<CurrencyRecord> after)
{
    Dictionary<string, CurrencyRecord> old = before.ToDictionary(static c => c.Code, StringComparer.Ordinal);
    List<string> changes = [];

    foreach (CurrencyRecord current in after)
    {
        if (!old.Remove(current.Code, out CurrencyRecord? was))
        {
            changes.Add($"+ {current.Code} added: {current.Name}, numeric {current.NumericCode}, {current.DecimalDigits} decimal digits");
            continue;
        }

        if (was == current)
        {
            continue;
        }

        if (was.Withdrawn is null && current.Withdrawn is not null)
        {
            changes.Add($"- {current.Code} withdrawn {current.Withdrawn}"
                      + (current.ReplacedBy is null ? string.Empty : $", replaced by {current.ReplacedBy}")
                      + " (kept, and marked [Obsolete])");
            continue;
        }

        foreach ((string field, object? from, object? to) in (ReadOnlySpan<(string, object?, object?)>)
        [
            ("numericCode", was.NumericCode, current.NumericCode),
            ("name", was.Name, current.Name),
            ("symbol", was.Symbol, current.Symbol),
            ("decimalDigits", was.DecimalDigits, current.DecimalDigits),
            ("minorUnitsPerMajor", was.MinorUnitsPerMajor, current.MinorUnitsPerMajor),
            ("cashDecimalDigits", was.CashDecimalDigits, current.CashDecimalDigits),
            ("cashRoundingStep", was.CashRoundingStep, current.CashRoundingStep),
            ("withdrawn", was.Withdrawn, current.Withdrawn),
            ("replacedBy", was.ReplacedBy, current.ReplacedBy)
        ])
        {
            if (!Equals(from, to))
            {
                changes.Add($"~ {current.Code} {field}: {from ?? "(none)"} -> {to ?? "(none)"}");
            }
        }
    }

    // Unreachable while shipped currencies are retained, but a removal must never pass silently.
    foreach (string removed in old.Keys)
    {
        changes.Add($"! {removed} removed — this deletes a public member and is a breaking change");
    }

    return changes;
}

static int Pow10(int exponent)
{
    int result = 1;
    for (int i = 0; i < exponent; i++)
    {
        result *= 10;
    }

    return result;
}

static byte ReadByte(JsonElement element, string property, byte fallback)
{
    return element.TryGetProperty(property, out JsonElement value) && byte.TryParse(value.GetString(), CultureInfo.InvariantCulture, out byte parsed)
        ? parsed
        : fallback;
}

static string? ReadName(JsonElement names, string code)
{
    return names.TryGetProperty(code, out JsonElement entry) && entry.TryGetProperty("displayName", out JsonElement displayName)
        ? displayName.GetString()
        : null;
}

static string? ReadSymbol(JsonElement names, string code)
{
    if (!names.TryGetProperty(code, out JsonElement entry))
    {
        return null;
    }

    // XXX's CLDR symbol is the generic currency placeholder '¤', which would be confusing in output.
    if (code is "XXX" or "XTS")
    {
        return code;
    }

    // The narrow form is the one people recognise ("kr" rather than "DKK"); fall back to the wide form.
    if (entry.TryGetProperty("symbol-alt-narrow", out JsonElement narrow))
    {
        return narrow.GetString();
    }

    return entry.TryGetProperty("symbol", out JsonElement symbol) ? symbol.GetString() : null;
}

static string Capitalise(string value)
{
    return string.IsNullOrEmpty(value) ? value : CultureInfo.InvariantCulture.TextInfo.ToTitleCase(value.ToLowerInvariant());
}

static (byte Digits, byte Step) CoerceCashIncrement(byte digits, int minorUnitsPerMajor, byte cashDigits, byte cashStep)
{
    decimal cashIncrement = cashStep / Pow10Decimal(cashDigits);
    decimal minorUnit = 1m / minorUnitsPerMajor;

    if (cashIncrement >= minorUnit)
    {
        return (cashDigits, cashStep);
    }

    decimal stepAtAccountingPrecision = Pow10Decimal(digits) / minorUnitsPerMajor;

    if (decimal.Truncate(stepAtAccountingPrecision) != stepAtAccountingPrecision || stepAtAccountingPrecision > byte.MaxValue)
    {
        throw new InvalidOperationException(
            $"A minor unit of 1/{minorUnitsPerMajor} cannot be expressed as a cash step at {digits} decimal digits.");
    }

    return (digits, (byte)stepAtAccountingPrecision);
}

static decimal Pow10Decimal(int exponent)
{
    decimal result = 1m;

    for (int i = 0; i < exponent; i++)
    {
        result *= 10m;
    }

    return result;
}

static void Validate(List<CurrencyRecord> currencies)
{
    HashSet<string> codes = new(StringComparer.Ordinal);
    HashSet<short> numerics = [];

    foreach (CurrencyRecord c in currencies)
    {
        if (c.Code.Length != 3 || !c.Code.All(char.IsAsciiLetterUpper))
        {
            throw new InvalidOperationException($"'{c.Code}' is not three uppercase ASCII letters.");
        }

        if (!codes.Add(c.Code))
        {
            throw new InvalidOperationException($"Duplicate alphabetic code '{c.Code}'.");
        }

        // Only among active currencies: ISO reuses a withdrawn currency's number for its successor —
        // ANG and XCG are both 532 — and the runtime's numeric lookup prefers the active one.
        if (c.Withdrawn is null && !numerics.Add(c.NumericCode))
        {
            throw new InvalidOperationException($"Duplicate numeric code {c.NumericCode} on '{c.Code}'.");
        }

        if (c.MinorUnitsPerMajor is not 0 && c.DecimalDigits > 4)
        {
            throw new InvalidOperationException($"'{c.Code}' has implausible precision {c.DecimalDigits}.");
        }

        if (c.MinorUnitsPerMajor is not 0)
        {
            // The check that would have caught the MRU cash increment before it was ever written.
            decimal cashUnits = (decimal)c.CashRoundingStep * c.MinorUnitsPerMajor / Pow10Decimal(c.CashDecimalDigits);

            if (cashUnits < 1m || decimal.Truncate(cashUnits) != cashUnits)
            {
                throw new InvalidOperationException(
                    $"'{c.Code}' has a cash increment of {c.CashRoundingStep}e-{c.CashDecimalDigits}, which is "
                  + $"{cashUnits} minor units. Cash cannot be finer than the currency's own unit.");
            }
        }
    }

    foreach (string required in (string[])["XXX", "XTS", "DKK", "EUR", "USD"])
    {
        if (!codes.Contains(required))
        {
            throw new InvalidOperationException($"Expected '{required}' to survive the filter.");
        }
    }

    foreach (string excluded in (string[])["XAU", "XAG", "XPT", "XPD", "XDR", "XSU", "XUA", "XBA", "XAD"])
    {
        if (codes.Contains(excluded))
        {
            throw new InvalidOperationException($"'{excluded}' is not money and must not be generated.");
        }
    }
}

static string FindRepositoryRoot()
{
    DirectoryInfo? dir = new(AppContext.BaseDirectory);
    while (dir is not null && !Directory.Exists(Path.Combine(dir.FullName, ".git")))
    {
        dir = dir.Parent;
    }

    return dir?.FullName ?? throw new InvalidOperationException("Could not locate the repository root.");
}
