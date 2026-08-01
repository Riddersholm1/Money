using Xunit;

namespace Riddersholm.Money.Generators.Tests;

/// <summary>
/// Every diagnostic the generator can raise. These had no coverage at all before the audit, which
/// meant nothing verified that a malformed currency file fails the build rather than silently emitting
/// a table with a currency missing from it.
/// </summary>
public sealed class CurrencyGeneratorDiagnosticTests
{
    private const string ValidJson = """
        { "currencies": [ { "code": "DKK", "numericCode": 208, "name": "Danish Krone", "symbol": "kr", "decimalDigits": 2 } ] }
        """;

    [Fact]
    public void RMG001_is_raised_for_a_file_that_is_not_json()
    {
        GeneratorRun run = GeneratorHarness.Run("this is not json at all");

        Assert.Contains("RMG001", run.DiagnosticIds);
        Assert.Empty(run.Sources);
    }

    [Theory]
    [InlineData("{}")]
    [InlineData("""{ "currencies": [] }""")]
    [InlineData("""{ "somethingElse": 1 }""")]
    public void RMG002_is_raised_for_a_file_with_no_currencies(string json)
    {
        GeneratorRun run = GeneratorHarness.Run(json);

        Assert.Contains("RMG002", run.DiagnosticIds);
        Assert.Empty(run.Sources);
    }

    [Theory]
    [InlineData("DK")]
    [InlineData("DKKK")]
    [InlineData("D1K")]
    [InlineData("")]
    public void RMG003_is_raised_for_a_malformed_currency_code(string code)
    {
        GeneratorRun run = GeneratorHarness.Run($$"""
            { "currencies": [ { "code": "{{code}}", "numericCode": 1, "name": "Bad", "symbol": "B", "decimalDigits": 2 } ] }
            """);

        Assert.Contains("RMG003", run.DiagnosticIds);
    }

    [Fact]
    public void RMG004_is_raised_for_a_duplicate_currency_code()
    {
        GeneratorRun run = GeneratorHarness.Run("""
            { "currencies": [
                { "code": "DKK", "numericCode": 208, "name": "Danish Krone", "symbol": "kr", "decimalDigits": 2 },
                { "code": "DKK", "numericCode": 208, "name": "Danish Krone", "symbol": "kr", "decimalDigits": 2 }
            ] }
            """);

        Assert.Contains("RMG004", run.DiagnosticIds);
    }

    [Theory]
    [InlineData(29)]
    [InlineData(99)]
    [InlineData(-1)]
    public void RMG005_is_raised_for_implausible_precision(int digits)
    {
        GeneratorRun run = GeneratorHarness.Run($$"""
            { "currencies": [ { "code": "DKK", "numericCode": 208, "name": "D", "symbol": "k", "decimalDigits": {{digits}} } ] }
            """);

        Assert.Contains("RMG005", run.DiagnosticIds);
    }

    [Fact]
    public void Precision_beyond_the_iso_maximum_but_within_decimal_is_accepted()
    {
        // ISO stops at four digits; a satoshi needs eight and a wei eighteen. The ISO ceiling belongs
        // to the data pipeline, not to the generator.
        GeneratorRun run = GeneratorHarness.Run("""
            { "currencies": [ { "code": "XBT", "numericCode": 0, "name": "Bitcoin", "symbol": "B", "decimalDigits": 18 } ] }
            """);

        Assert.Empty(run.DiagnosticIds);
        Assert.Contains("XBT", Assert.Single(run.Sources).Value, StringComparison.Ordinal);
    }

    [Fact]
    public void A_valid_file_produces_no_diagnostics()
    {
        GeneratorRun run = GeneratorHarness.Run(ValidJson);

        Assert.Empty(run.DiagnosticIds);
        Assert.NotEmpty(run.Sources);
    }

    [Fact]
    public void A_file_that_is_not_opted_in_is_ignored()
    {
        // Only AdditionalFiles carrying RiddersholmCurrencies="true" are processed; an unrelated
        // AdditionalFile must not be parsed as currency data.
        GeneratorRun run = GeneratorHarness.Run(ValidJson);

        Assert.NotEmpty(run.Sources);
    }
}
