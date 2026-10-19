using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Xunit;

namespace Riddersholm.Money.Generators.Tests;

/// <summary>
/// The generator's handling of malformed and unusual currency data, from the production-readiness review.
/// </summary>
/// <remarks>
/// Before this review, a value that was present but wrong became a default or was truncated into range:
/// a cash step of 256 was cast to a byte and became 0, then 1; a numeric code of 70000 wrapped to 4464; a
/// missing <c>decimalDigits</c> produced a currency with no decimals. Every case below built cleanly and
/// produced a currency that rounds money wrongly.
/// </remarks>
public sealed class ProductionReadinessGeneratorTests
{
    private static string File(string properties) => $$"""
        { "currencies": [ { "code": "QQQ", "name": "Test", "symbol": "Q", {{properties}} } ] }
        """;

    [Theory]
    [InlineData("\"numericCode\": 1")]                                    // decimalDigits missing entirely
    [InlineData("\"decimalDigits\": 2.5")]
    [InlineData("\"decimalDigits\": \"two\"")]
    [InlineData("\"decimalDigits\": true")]
    [InlineData("\"decimalDigits\": 2, \"numericCode\": 1000")]
    [InlineData("\"decimalDigits\": 2, \"numericCode\": 70000")]
    [InlineData("\"decimalDigits\": 2, \"numericCode\": -1")]
    [InlineData("\"decimalDigits\": 2, \"cashRoundingStep\": 256")]
    [InlineData("\"decimalDigits\": 2, \"cashRoundingStep\": -1")]
    [InlineData("\"decimalDigits\": 2, \"cashDecimalDigits\": 29")]
    [InlineData("\"decimalDigits\": 2, \"minorUnitsPerMajor\": 3")]       // thirds have no decimal form
    [InlineData("\"decimalDigits\": 2, \"minorUnitsPerMajor\": 1000")]    // finer than two places
    [InlineData("\"decimalDigits\": 28, \"minorUnitsPerMajor\": 7")]
    [InlineData("\"decimalDigits\": 18, \"minorUnitsPerMajor\": 1000000000000000001")]
    [InlineData("\"decimalDigits\": 20")]                                  // 10^20 does not fit; must be explicit
    [InlineData("\"decimalDigits\": 2, \"name\": 5")]
    [InlineData("\"decimalDigits\": 2, \"withdrawn\": \"2026/01\"")]
    [InlineData("\"decimalDigits\": 2, \"withdrawn\": \"2026-13\"")]
    [InlineData("\"decimalDigits\": 2, \"withdrawn\": 2026")]
    [InlineData("\"decimalDigits\": 2, \"replacedBy\": \"EUR\"")]          // a replacement implies a withdrawal
    [InlineData("\"decimalDigits\": 2, \"withdrawn\": \"2026-01\", \"replacedBy\": \"EURO\"")]
    public void RMG006_is_raised_for_a_missing_or_invalid_property(string properties)
    {
        GeneratorRun run = GeneratorHarness.Run(File(properties), emitCore: true);

        Assert.Contains("RMG006", run.DiagnosticIds);
        Assert.Empty(run.Sources);
    }

    [Fact]
    public void An_integer_too_large_for_a_double_is_read_exactly()
    {
        // As a double, 999999999999999999 is exactly 10^18 — which divides 10^18 and would have been
        // accepted as a different, valid currency. Read exactly, it is refused.
        GeneratorRun run = GeneratorHarness.Run(File("\"decimalDigits\": 18, \"minorUnitsPerMajor\": 999999999999999999"), emitCore: true);

        Assert.Contains("RMG006", run.DiagnosticIds);
    }

    [Theory]
    [InlineData("\"decimalDigits\": 2", "100L")]
    [InlineData("\"decimalDigits\": \"2\"", "100L")]                       // a quoted integer is unambiguous
    [InlineData("\"decimalDigits\": 2.0", "100L")]                         // an integral number is an integer
    [InlineData("\"decimalDigits\": 2, \"cashRoundingStep\": 0", "100L")]  // CLDR's "no special rounding"
    [InlineData("\"decimalDigits\": 20, \"minorUnitsPerMajor\": 1000000000000000000", "1000000000000000000L")]
    [InlineData("\"decimalDigits\": 28, \"minorUnitsPerMajor\": 1000000000000000000", "1000000000000000000L")]
    public void Valid_values_still_generate(string properties, string expectedMinorUnits)
    {
        GeneratorRun run = GeneratorHarness.Run(File(properties), emitCore: true);

        Assert.Empty(run.DiagnosticIds);
        Assert.Contains(expectedMinorUnits, run.Source("Currency.Generated.g.cs")!, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(true, "Currency.Generated.g.cs")]
    [InlineData(false, ".Currencies.g.cs")]
    public void Names_with_line_breaks_and_control_characters_cannot_escape_their_literal(bool emitCore, string hintName)
    {
        // One \n in the JSON used to end the string literal and the /// comment it was written into,
        // and the remainder of the name was compiled as code.
        string json = File("\"decimalDigits\": 2").Replace(
            "\"name\": \"Test\"",
            "\"name\": \"Line|n} public static int Injected => 1; //|u2028|tEnd\\\"\"",
            StringComparison.Ordinal).Replace('|', '\\');

        string source = GeneratorHarness.Run(json, emitCore).Source(hintName)!;

        Assert.DoesNotContain(
            CSharpSyntaxTree
                // Preview, because the Roslyn this project pins predates C# 14 and only parses the
                // extension blocks the consumer-side output uses under that setting.
                .ParseText(source, new CSharpParseOptions(LanguageVersion.Preview), cancellationToken: TestContext.Current.CancellationToken)
                .GetDiagnostics(TestContext.Current.CancellationToken),
            d => d.Severity == DiagnosticSeverity.Error);
        Assert.DoesNotContain("\npublic static int Injected", source.Replace("\r", string.Empty, StringComparison.Ordinal).Replace(" ", string.Empty, StringComparison.Ordinal), StringComparison.Ordinal);
        Assert.Contains("\\u000A", source, StringComparison.Ordinal);
        Assert.Contains("\\u2028", source, StringComparison.Ordinal);
        Assert.Contains("\\u0009", source, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(true, "Currency.Generated.g.cs")]
    [InlineData(false, ".Currencies.g.cs")]
    public void A_withdrawn_currency_is_kept_and_marked_obsolete(bool emitCore, string hintName)
    {
        // Deleting a withdrawn currency would remove a public member — a breaking change — and strand
        // every stored amount in it without a precision to round by.
        GeneratorRun run = GeneratorHarness.Run("""
            { "currencies": [
                { "code": "BGN", "numericCode": 975, "name": "Bulgarian Lev", "symbol": "BGN", "decimalDigits": 2, "withdrawn": "2026-01", "replacedBy": "EUR" },
                { "code": "DKK", "numericCode": 208, "name": "Danish Krone", "symbol": "kr", "decimalDigits": 2 }
            ] }
            """, emitCore);

        Assert.Empty(run.DiagnosticIds);

        string source = run.Source(hintName)!;
        string[] lines = source.Split('\n');
        int bgn = Array.FindIndex(lines, line => line.Contains("public static Currency BGN", StringComparison.Ordinal));
        int dkk = Array.FindIndex(lines, line => line.Contains("public static Currency DKK", StringComparison.Ordinal));

        Assert.Contains("[global::System.Obsolete(", lines[bgn - 1], StringComparison.Ordinal);
        Assert.Contains("withdrawn from ISO 4217 in 2026-01 and replaced by EUR", lines[bgn - 1], StringComparison.Ordinal);
        Assert.DoesNotContain("Obsolete", lines[dkk - 1], StringComparison.Ordinal);
        Assert.Contains("Withdrawn from ISO 4217 in 2026-01; replaced by EUR.", source, StringComparison.Ordinal);

        // Still a real currency with metadata, so historical amounts keep their precision.
        Assert.Contains(emitCore ? "\"BGN\"" : "code: \"BGN\"", source, StringComparison.Ordinal);

        if (emitCore)
        {
            // The runtime reads this to give a shared numeric code to the active currency.
            int flags = Array.FindIndex(lines, line => line.Contains("ReadOnlySpan<byte> Withdrawn", StringComparison.Ordinal));
            Assert.Equal("1, 0,", lines[flags + 2].Trim());
        }
    }
}
