using System.Collections.Immutable;

using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Diagnostics;
using Microsoft.CodeAnalysis.Text;

using Xunit;

namespace OpenApiWeaver.Tests;

public sealed partial class OpenApiWeaverSourceGeneratorTests
{
    private static string GenerateSource(string openApi)
    {
        var result = RunGenerator(openApi);
        Assert.DoesNotContain(result.Diagnostics, static diagnostic => diagnostic.Severity == DiagnosticSeverity.Error);
        return Assert.Single(result.GeneratedSources);
    }

    private static string GenerateSource(string openApi, IReadOnlyDictionary<string, string> additionalFileOptions)
    {
        var result = RunGenerator(openApi, additionalFileOptions);
        Assert.DoesNotContain(result.Diagnostics, static diagnostic => diagnostic.Severity == DiagnosticSeverity.Error);
        return Assert.Single(result.GeneratedSources);
    }

    private static readonly ImmutableArray<MetadataReference> s_metadataReferences = [..
        ((string?)AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES"))!
            .Split(Path.PathSeparator)
            .Distinct()
            .Select(static path => (MetadataReference)MetadataReference.CreateFromFile(path))];

    private static GeneratorTestResult RunGenerator(string openApi, IReadOnlyDictionary<string, string>? additionalFileOptions = null)
    {
        var generator = new OpenApiWeaverSourceGenerator();
        var parseOptions = new CSharpParseOptions(LanguageVersion.Preview);
        var syntaxTree = CSharpSyntaxTree.ParseText("public sealed class Marker {}", parseOptions);

        var compilation = CSharpCompilation.Create(
            "GeneratorTests",
            [syntaxTree],
            s_metadataReferences,
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));

        var additionalText = new InMemoryAdditionalText("test.yaml", openApi);
        var optionsProvider = new TestAnalyzerConfigOptionsProvider(
            globalOptions: new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["build_property.RootNamespace"] = "GeneratorTests"
            },
            additionalFileOptions is null
                ? null
                : new Dictionary<string, IReadOnlyDictionary<string, string>>(StringComparer.Ordinal)
                {
                    [additionalText.Path] = additionalFileOptions
                });

        GeneratorDriver driver = CSharpGeneratorDriver.Create(
            generators: [generator.AsSourceGenerator()],
            additionalTexts: [additionalText],
            parseOptions: parseOptions,
            optionsProvider: optionsProvider);

        driver = driver.RunGeneratorsAndUpdateCompilation(compilation, out var outputCompilation, out var diagnostics);

        var generatorDiagnostics = driver.GetRunResult()
            .Results
            .SelectMany(static result => result.Diagnostics)
            .ToArray();

        var compilationErrors = diagnostics
            .Concat(outputCompilation.GetDiagnostics())
            .Where(static diagnostic => diagnostic.Severity == DiagnosticSeverity.Error
                && diagnostic.Id != "OAW001"
                && diagnostic.Id != "OAW003"
                && diagnostic.Id != "OAW004")
            .ToArray();

        Assert.True(compilationErrors.Length == 0, string.Join(Environment.NewLine, compilationErrors.Select(static error => error.ToString())));

        var generatedSources = driver.GetRunResult()
            .Results
            .SelectMany(static result => result.GeneratedSources)
            .Select(static source => source.SourceText.ToString())
            .ToArray();

        return new GeneratorTestResult(generatorDiagnostics, generatedSources);
    }

    private sealed class InMemoryAdditionalText(string path, string content) : AdditionalText
    {
        public override string Path { get; } = path;

        public override SourceText GetText(CancellationToken cancellationToken = default)
            => SourceText.From(content);
    }

    private sealed class TestAnalyzerConfigOptionsProvider(
        IReadOnlyDictionary<string, string> globalOptions,
        IReadOnlyDictionary<string, IReadOnlyDictionary<string, string>>? additionalFileOptions) : AnalyzerConfigOptionsProvider
    {
        private readonly IReadOnlyDictionary<string, IReadOnlyDictionary<string, string>> _additionalFileOptions =
            additionalFileOptions ?? new Dictionary<string, IReadOnlyDictionary<string, string>>(StringComparer.Ordinal);

        public override AnalyzerConfigOptions GlobalOptions { get; } = new DictionaryAnalyzerConfigOptions(globalOptions);

        public override AnalyzerConfigOptions GetOptions(SyntaxTree tree)
            => DictionaryAnalyzerConfigOptions.Empty;

        public override AnalyzerConfigOptions GetOptions(AdditionalText textFile)
            => _additionalFileOptions.TryGetValue(textFile.Path, out var options)
                ? new DictionaryAnalyzerConfigOptions(options)
                : DictionaryAnalyzerConfigOptions.Empty;
    }

    private sealed class DictionaryAnalyzerConfigOptions(IReadOnlyDictionary<string, string> options) : AnalyzerConfigOptions
    {
        public static DictionaryAnalyzerConfigOptions Empty { get; } = new(new Dictionary<string, string>(StringComparer.Ordinal));

        public override bool TryGetValue(string key, out string value)
        {
            if (options.TryGetValue(key, out var configuredValue))
            {
                value = configuredValue;
                return true;
            }

            value = string.Empty;
            return false;
        }
    }

    private sealed class GeneratorTestResult(
        IReadOnlyList<Diagnostic> diagnostics,
        IReadOnlyList<string> generatedSources)
    {
        public IReadOnlyList<Diagnostic> Diagnostics { get; } = diagnostics;

        public IReadOnlyList<string> GeneratedSources { get; } = generatedSources;
    }
}
