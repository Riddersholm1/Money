using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization;
using Riddersholm.Money.DataSync;

// Refreshes eng/iso-4217.json from the ISO 4217 register and the Unicode CLDR.
//
// Run manually when ISO publishes an amendment; the output is committed so that builds are
// offline-reproducible and currency changes show up as a reviewable data diff rather than a
// regenerated blob of C#.

const string IsoUrl = "https://raw.githubusercontent.com/datasets/currency-codes/main/data/codes-all.csv";
const string CldrFractionsUrl = "https://raw.githubusercontent.com/unicode-org/cldr-json/main/cldr-json/cldr-core/supplemental/currencyData.json";
const string CldrNamesUrl = "https://raw.githubusercontent.com/unicode-org/cldr-json/main/cldr-json/cldr-numbers-full/main/en/currencies.json";

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
    ["MGA"] = 5,
};

string repoRoot = FindRepositoryRoot();
string outputPath = Path.Combine(repoRoot, "eng", "iso-4217.json");

using HttpClient http = new() { Timeout = TimeSpan.FromMinutes(2) };

Console.WriteLine("Downloading ISO 4217 register...");
string csv = await http.GetStringAsync(new Uri(IsoUrl)).ConfigureAwait(false);

Console.WriteLine("Downloading CLDR fraction data...");
using JsonDocument fractionsDoc = JsonDocument.Parse(await http.GetStringAsync(new Uri(CldrFractionsUrl)).ConfigureAwait(false));

Console.WriteLine("Downloading CLDR currency names...");
using JsonDocument namesDoc = JsonDocument.Parse(await http.GetStringAsync(new Uri(CldrNamesUrl)).ConfigureAwait(false));

JsonElement fractions = fractionsDoc.RootElement
    .GetProperty("supplemental").GetProperty("currencyData").GetProperty("fractions");

JsonElement names = namesDoc.RootElement
    .GetProperty("main").GetProperty("en").GetProperty("numbers").GetProperty("currencies");

JsonElement defaultFraction = fractions.GetProperty("DEFAULT");
byte defaultDigits = ReadByte(defaultFraction, "_digits", 2);

// Collapse the country-per-row ISO list into one row per currency.
Dictionary<string, Dictionary<string, string>> byCode = new(StringComparer.Ordinal);
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
        continue;
    }

    byCode.TryAdd(code, row);
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
        CashRoundingStep = cashStep == 0 ? (byte)1 : cashStep,
    });
}

currencies.Sort(static (a, b) => string.CompareOrdinal(a.Code, b.Code));

Validate(currencies);

CurrencyDataFile file = new()
{
    Source = new DataSource
    {
        Iso = IsoUrl,
        CldrFractions = CldrFractionsUrl,
        CldrNames = CldrNamesUrl,
        RetrievedUtc = DateOnly.FromDateTime(DateTime.UtcNow),
        Filter = "Active ISO 4217 codes with a numeric minor unit (excludes metals, bond units, SDR "
               + "and other funds), minus XAD (unit of account), plus XTS and XXX.",
    },
    Currencies = currencies,
};

JsonSerializerOptions options = new(JsonSerializerDefaults.Web)
{
    WriteIndented = true,
    DefaultIgnoreCondition = JsonIgnoreCondition.Never,
    // Write '₪' rather than '₪'. This file is reviewed by humans in diffs, and it is data —
    // never interpolated into HTML — so the relaxed encoder carries no injection risk here.
    Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
};

Directory.CreateDirectory(Path.GetDirectoryName(outputPath)!);
await File.WriteAllTextAsync(outputPath, JsonSerializer.Serialize(file, options) + Environment.NewLine).ConfigureAwait(false);

Console.WriteLine($"Wrote {currencies.Count} currencies to {outputPath}");
return 0;

static int Pow10(int exponent)
{
    int result = 1;
    for (int i = 0; i < exponent; i++)
    {
        result *= 10;
    }

    return result;
}

static byte ReadByte(JsonElement element, string property, byte fallback) =>
    element.TryGetProperty(property, out JsonElement value)
    && byte.TryParse(value.GetString(), CultureInfo.InvariantCulture, out byte parsed)
        ? parsed
        : fallback;

static string? ReadName(JsonElement names, string code) =>
    names.TryGetProperty(code, out JsonElement entry)
    && entry.TryGetProperty("displayName", out JsonElement displayName)
        ? displayName.GetString()
        : null;

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

static string Capitalise(string value) =>
    string.IsNullOrEmpty(value) ? value : CultureInfo.InvariantCulture.TextInfo.ToTitleCase(value.ToLowerInvariant());

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

        if (!numerics.Add(c.NumericCode))
        {
            throw new InvalidOperationException($"Duplicate numeric code {c.NumericCode} on '{c.Code}'.");
        }

        if (c.MinorUnitsPerMajor is not 0 && c.DecimalDigits > 4)
        {
            throw new InvalidOperationException($"'{c.Code}' has implausible precision {c.DecimalDigits}.");
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
