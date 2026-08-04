using System.Text;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Diagnostics;
using Microsoft.CodeAnalysis.Text;

namespace Riddersholm.Money.Generators;

/// <summary>
/// Turns ISO 4217 currency definition files into strongly typed <c>Currency</c> members.
/// </summary>
/// <remarks>
/// Opt a file in from MSBuild:
/// <code>
/// &lt;ItemGroup&gt;
///   &lt;AdditionalFiles Include="my-currencies.json" RiddersholmCurrencies="true" /&gt;
/// &lt;/ItemGroup&gt;
/// </code>
/// </remarks>
[Generator(LanguageNames.CSharp)]
public sealed class CurrencyGenerator : IIncrementalGenerator
{
    private const string OptInMetadata = "build_metadata.AdditionalFiles.RiddersholmCurrencies";
    private const string EmitCoreProperty = "build_property.RiddersholmMoneyEmitCore";
    private const string RootNamespaceProperty = "build_property.RootNamespace";

    /// <inheritdoc />
    public void Initialize(IncrementalGeneratorInitializationContext context)
    {
        IncrementalValuesProvider<CurrencyFile> files = context.AdditionalTextsProvider
            .Combine(context.AnalyzerConfigOptionsProvider)
            .Where(static pair => IsOptedIn(pair.Left, pair.Right))
            .Select(static (pair, cancellationToken) =>
            {
                AnalyzerConfigOptions global = pair.Right.GlobalOptions;

                return new CurrencyFile(
                    Path: pair.Left.Path,
                    // The pipeline caches on this record's value. Carrying the text as a string means
                    // the generator only re-runs when the file's content actually changes, not on
                    // every keystroke elsewhere in the project.
                    Content: pair.Left.GetText(cancellationToken)?.ToString() ?? string.Empty,
                    EmitCore: global.TryGetValue(EmitCoreProperty, out string? emitCore)
                              && string.Equals(emitCore, "true", StringComparison.OrdinalIgnoreCase),
                    RootNamespace: global.TryGetValue(RootNamespaceProperty, out string? ns) ? ns : string.Empty);
            });

        context.RegisterSourceOutput(files, static (productionContext, file) => Execute(productionContext, file));
    }

    private static bool IsOptedIn(AdditionalText text, AnalyzerConfigOptionsProvider options) =>
        options.GetOptions(text).TryGetValue(OptInMetadata, out string? value)
        && string.Equals(value, "true", StringComparison.OrdinalIgnoreCase);

    private static void Execute(SourceProductionContext context, CurrencyFile file)
    {
        List<Diagnostic> diagnostics = [];
        IReadOnlyList<CurrencyDefinition> currencies =
            CurrencyDefinitionReader.Read(file.Path, file.Content, diagnostics);

        foreach (Diagnostic diagnostic in diagnostics)
        {
            context.ReportDiagnostic(diagnostic);
        }

        if (currencies.Count == 0)
        {
            return;
        }

        string fileName = Path.GetFileNameWithoutExtension(file.Path);

        if (file.EmitCore)
        {
            context.AddSource("Currency.Generated.g.cs", SourceText.From(CoreEmitter.Emit(currencies), Encoding.UTF8));
            return;
        }

        string containerName = ExtensionEmitter.ToIdentifier(fileName);
        string source = ExtensionEmitter.Emit(containerName, file.RootNamespace, currencies);

        context.AddSource($"{containerName}.Currencies.g.cs", SourceText.From(source, Encoding.UTF8));
    }

    /// <summary>A currency definition file plus the build context needed to emit for it.</summary>
    private sealed record CurrencyFile(string Path, string Content, bool EmitCore, string RootNamespace);
}
