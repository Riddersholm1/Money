using Xunit;

namespace Riddersholm.Money.Generators.Tests;

/// <summary>What the generator actually emits, in both of its modes.</summary>
public sealed class CurrencyGeneratorOutputTests
{
    private const string TwoCurrencies = """
        { "currencies": [
            { "code": "DKK", "numericCode": 208, "name": "Danish Krone", "symbol": "kr", "decimalDigits": 2 },
            { "code": "JPY", "numericCode": 392, "name": "Japanese Yen", "symbol": "¥", "decimalDigits": 0 }
        ] }
        """;

    [Fact]
    public void Core_mode_emits_currency_constants_and_a_lookup_table()
    {
        string source = GeneratorHarness.Run(TwoCurrencies, emitCore: true).Source("Currency.Generated.g.cs")!;

        Assert.Contains("public readonly partial record struct Currency", source, StringComparison.Ordinal);
        Assert.Contains("public static Currency DKK =>", source, StringComparison.Ordinal);
        Assert.Contains("internal static class CurrencyTable", source, StringComparison.Ordinal);
        Assert.Contains("public const int Count = 2;", source, StringComparison.Ordinal);
        Assert.Contains("public static bool TryGetOrdinal(uint packed, out int ordinal)", source, StringComparison.Ordinal);
        Assert.Contains("ReadOnlySpan<byte> DecimalDigits", source, StringComparison.Ordinal);
    }

    [Fact]
    public void Extension_mode_emits_extension_members_and_a_module_initializer()
    {
        string source = GeneratorHarness.Run(TwoCurrencies).Source(".Currencies.g.cs")!;

        Assert.Contains("extension(Currency)", source, StringComparison.Ordinal);
        Assert.Contains("public static Currency DKK =>", source, StringComparison.Ordinal);
        Assert.Contains("[ModuleInitializer]", source, StringComparison.Ordinal);
        Assert.Contains("CurrencyRegistry.Register(", source, StringComparison.Ordinal);
        Assert.Contains("namespace Contoso.Billing;", source, StringComparison.Ordinal);
    }

    [Fact]
    public void Extension_mode_names_the_container_after_the_file()
    {
        Assert.Contains(
            "public static class Currencies",
            GeneratorHarness.Run(TwoCurrencies).Source(".Currencies.g.cs")!,
            StringComparison.Ordinal);
    }

    [Fact]
    public void Output_is_ordered_by_code_so_it_is_deterministic()
    {
        // Emitting in file order would make an unrelated reordering of the data look like a code change.
        string source = GeneratorHarness.Run("""
            { "currencies": [
                { "code": "ZWL", "numericCode": 932, "name": "Z", "symbol": "Z", "decimalDigits": 2 },
                { "code": "AED", "numericCode": 784, "name": "A", "symbol": "A", "decimalDigits": 2 }
            ] }
            """, emitCore: true).Source("Currency.Generated.g.cs")!;

        Assert.True(
            source.IndexOf("Currency AED", StringComparison.Ordinal) < source.IndexOf("Currency ZWL", StringComparison.Ordinal),
            "Currencies should be emitted in ordinal code order.");
    }

    [Fact]
    public void The_same_input_always_produces_byte_identical_output()
    {
        Assert.Equal(
            GeneratorHarness.Run(TwoCurrencies, emitCore: true).Source("Currency.Generated.g.cs"),
            GeneratorHarness.Run(TwoCurrencies, emitCore: true).Source("Currency.Generated.g.cs"));
    }

    [Fact]
    public void Currency_names_containing_xml_significant_characters_are_escaped()
    {
        // "São Tomé & Príncipe Dobra" and "Trinidad & Tobago Dollar" both carry a bare ampersand, which
        // is invalid inside an XML documentation comment and broke the documentation build.
        string source = GeneratorHarness.Run("""
            { "currencies": [ { "code": "STN", "numericCode": 930, "name": "São Tomé & Príncipe <Dobra>", "symbol": "Db", "decimalDigits": 2 } ] }
            """, emitCore: true).Source("Currency.Generated.g.cs")!;

        // The two contexts need opposite treatment, and the generator must not confuse them.
        string docComment = source.Split('\n').First(line => line.Contains("/// <summary>", StringComparison.Ordinal));

        Assert.Contains("&amp;", docComment, StringComparison.Ordinal);
        Assert.Contains("&lt;Dobra&gt;", docComment, StringComparison.Ordinal);
        Assert.DoesNotContain("& Príncipe", docComment, StringComparison.Ordinal);

        // A C# string literal wants the raw text; XML escaping there would corrupt the currency name
        // that GetName hands back at runtime.
        Assert.Contains("\"São Tomé & Príncipe <Dobra>\"", source, StringComparison.Ordinal);
    }

    [Fact]
    public void Currency_names_containing_quotes_are_escaped_in_string_literals()
    {
        string source = GeneratorHarness.Run("""
            { "currencies": [ { "code": "AAA", "numericCode": 1, "name": "The \"Best\" Dollar", "symbol": "A", "decimalDigits": 2 } ] }
            """, emitCore: true).Source("Currency.Generated.g.cs")!;

        Assert.Contains("\\\"Best\\\"", source, StringComparison.Ordinal);
    }

    [Fact]
    public void An_omitted_minor_unit_defaults_to_the_matching_power_of_ten()
    {
        string source = GeneratorHarness.Run("""
            { "currencies": [ { "code": "KWD", "numericCode": 414, "name": "Kuwaiti Dinar", "symbol": "K", "decimalDigits": 3 } ] }
            """, emitCore: true).Source("Currency.Generated.g.cs")!;

        Assert.Contains("1000L", source, StringComparison.Ordinal);
    }

    [Fact]
    public void An_explicit_minor_unit_overrides_the_power_of_ten()
    {
        // MRU and MGA divide by five despite recording two decimal digits.
        string source = GeneratorHarness.Run("""
            { "currencies": [ { "code": "MRU", "numericCode": 929, "name": "Ouguiya", "symbol": "M", "decimalDigits": 2, "minorUnitsPerMajor": 5 } ] }
            """, emitCore: true).Source("Currency.Generated.g.cs")!;

        Assert.Contains("5L", source, StringComparison.Ordinal);
    }

    [Fact]
    public void The_pipeline_caches_when_nothing_changes()
    {
        // An incremental generator that recomputes on every keystroke is a build-performance bug, and
        // it is invisible without an explicit assertion.
        Assert.True(GeneratorHarness.SecondRunIsCached(TwoCurrencies, emitCore: true));
    }
}
