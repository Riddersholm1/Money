using System.Collections.Immutable;
using System.Text;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Diagnostics;
using Microsoft.CodeAnalysis.Text;

namespace Riddersholm.Money.Generators.Tests;

/// <summary>The outcome of one generator run.</summary>
internal sealed record GeneratorRun(
    ImmutableArray<Diagnostic> Diagnostics,
    IReadOnlyDictionary<string, string> Sources)
{
    public string? Source(string hintNameSuffix) =>
        Sources.FirstOrDefault(pair => pair.Key.EndsWith(hintNameSuffix, StringComparison.Ordinal)).Value;

    public IEnumerable<string> DiagnosticIds => Diagnostics.Select(d => d.Id);
}

/// <summary>
/// Drives <see cref="CurrencyGenerator"/> the way the compiler does.
/// </summary>
/// <remarks>
/// The generator reads its input from <c>AdditionalFiles</c> and its mode from MSBuild properties, so
/// exercising it needs stand-ins for both. Without this harness the generator's diagnostics are
/// unreachable from a test — which is exactly why they had no coverage.
/// </remarks>
internal static class GeneratorHarness
{
    public static GeneratorRun Run(string currencyJson, bool emitCore = false, string rootNamespace = "Contoso.Billing")
    {
        var compilation = CSharpCompilation.Create(
            assemblyName: "GeneratorTests",
            syntaxTrees: [],
            references: [MetadataReference.CreateFromFile(typeof(object).Assembly.Location)],
            options: new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));

        GeneratorDriver driver = CSharpGeneratorDriver.Create(
            generators: [new CurrencyGenerator().AsSourceGenerator()],
            additionalTexts: [new StubAdditionalText("currencies.json", currencyJson)],
            optionsProvider: new StubOptionsProvider(emitCore, rootNamespace),
            driverOptions: new GeneratorDriverOptions(IncrementalGeneratorOutputKind.None, trackIncrementalGeneratorSteps: true));

        driver = driver.RunGeneratorsAndUpdateCompilation(compilation, out _, out _);
        GeneratorDriverRunResult result = driver.GetRunResult();

        return new GeneratorRun(
            result.Diagnostics,
            result.Results
                .SelectMany(r => r.GeneratedSources)
                .ToDictionary(s => s.HintName, s => s.SourceText.ToString(), StringComparer.Ordinal));
    }

    /// <summary>Runs twice over identical input and reports whether the second run reused cached output.</summary>
    public static bool SecondRunIsCached(string currencyJson, bool emitCore = false)
    {
        StubAdditionalText file = new("currencies.json", currencyJson);

        var compilation = CSharpCompilation.Create(
            assemblyName: "GeneratorTests",
            syntaxTrees: [],
            references: [MetadataReference.CreateFromFile(typeof(object).Assembly.Location)],
            options: new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));

        GeneratorDriver driver = CSharpGeneratorDriver.Create(
            generators: [new CurrencyGenerator().AsSourceGenerator()],
            additionalTexts: [file],
            optionsProvider: new StubOptionsProvider(emitCore, "Contoso.Billing"),
            driverOptions: new GeneratorDriverOptions(IncrementalGeneratorOutputKind.None, trackIncrementalGeneratorSteps: true));

        driver = driver.RunGenerators(compilation);
        driver = driver.RunGenerators(compilation);

        // Every tracked step of the second run should report that nothing had to be recomputed.
        return driver.GetRunResult().Results
            .SelectMany(r => r.TrackedSteps.Values)
            .SelectMany(steps => steps)
            .SelectMany(step => step.Outputs)
            .All(output => output.Reason is IncrementalStepRunReason.Cached or IncrementalStepRunReason.Unchanged);
    }

    private sealed class StubAdditionalText(string path, string text) : AdditionalText
    {
        public override string Path { get; } = path;

        public override SourceText GetText(CancellationToken cancellationToken = default) =>
            SourceText.From(text, Encoding.UTF8);
    }

    private sealed class StubOptionsProvider(bool emitCore, string rootNamespace) : AnalyzerConfigOptionsProvider
    {
        public override AnalyzerConfigOptions GlobalOptions { get; } = new StubOptions(new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["build_property.RiddersholmMoneyEmitCore"] = emitCore ? "true" : "false",
            ["build_property.RootNamespace"] = rootNamespace
        });

        public override AnalyzerConfigOptions GetOptions(SyntaxTree tree) => StubOptions.Empty;

        // This is the opt-in the generator looks for; without it no file is processed at all.
        public override AnalyzerConfigOptions GetOptions(AdditionalText textFile) =>
            new StubOptions(new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["build_metadata.AdditionalFiles.RiddersholmCurrencies"] = "true"
            });
    }

    private sealed class StubOptions(Dictionary<string, string> values) : AnalyzerConfigOptions
    {
        public static StubOptions Empty { get; } = new([]);

        public override bool TryGetValue(string key, out string value) => values.TryGetValue(key, out value!);
    }
}
