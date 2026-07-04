using System.Collections.Immutable;
using System.Reflection;
using System.Runtime.Loader;

using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Diagnostics;
using Microsoft.CodeAnalysis.Text;

using Xunit;

namespace OpenApiWeaver.Tests;

public sealed partial class ClientGeneratorTests
{
    private const string BuildMetadataAdditionalFilesClientName = "build_metadata.AdditionalFiles.ClientName";
    private const string BuildMetadataAdditionalFilesNamespace = "build_metadata.AdditionalFiles.Namespace";
    private const string BuildMetadataAdditionalFilesItemKind = "build_metadata.AdditionalFiles.OpenApiWeaverItemKind";
    private const string OpenApiWeaverDocumentItemKind = "Document";
    private const string DocumentEmptyDiagnosticId = "OAW001";
    private const string DocumentHasWarningsDiagnosticId = "OAW002";
    private const string DocumentInvalidDiagnosticId = "OAW003";
    private const string DocumentUnsupportedDiagnosticId = "OAW004";
    private const string RequestBodyUnsupportedDiagnosticId = "OAW005";
    private const string DiscriminatorUnsupportedDiagnosticId = "OAW006";
    private const string SchemaUnsupportedDiagnosticId = "OAW007";
    private const string ParameterUnsupportedDiagnosticId = "OAW008";

    private static readonly ImmutableHashSet<string> s_knownGeneratorDiagnosticIds = ImmutableHashSet.Create(
        StringComparer.Ordinal,
        DocumentEmptyDiagnosticId,
        DocumentHasWarningsDiagnosticId,
        DocumentInvalidDiagnosticId,
        DocumentUnsupportedDiagnosticId,
        RequestBodyUnsupportedDiagnosticId,
        DiscriminatorUnsupportedDiagnosticId,
        SchemaUnsupportedDiagnosticId,
        ParameterUnsupportedDiagnosticId);

    private static string GenerateSource(string openApi)
    {
        var result = RunGenerator(openApi);
        AssertNoErrorDiagnostics(result);
        return string.Join(Environment.NewLine, result.GeneratedSources);
    }

    private static string GenerateSource(string openApi, IReadOnlyDictionary<string, string> additionalFileOptions)
    {
        var result = RunGenerator(openApi, additionalFileOptions);
        AssertNoErrorDiagnostics(result);
        return string.Join(Environment.NewLine, result.GeneratedSources);
    }

    private static readonly ImmutableArray<MetadataReference> s_metadataReferences = [..
        ((string?)AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES"))!
            .Split(Path.PathSeparator)
            .Distinct()
            .Select(static path => (MetadataReference)MetadataReference.CreateFromFile(path))];

    private static GeneratorTestResult RunGenerator(
        string openApi,
        IReadOnlyDictionary<string, string>? additionalFileOptions = null,
        bool isOpenApiWeaverDocument = true)
    {
        var generator = new ClientGenerator();
        var parseOptions = new CSharpParseOptions(LanguageVersion.Preview);
        var syntaxTree = CSharpSyntaxTree.ParseText("public sealed class Marker {}", parseOptions);

        var compilation = CSharpCompilation.Create(
            "GeneratorTests",
            [syntaxTree],
            s_metadataReferences,
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));

        var additionalText = new InMemoryAdditionalText("test.yaml", openApi);
        var effectiveAdditionalFileOptions = CreateAdditionalFileOptions(additionalFileOptions, isOpenApiWeaverDocument);
        var optionsProvider = new TestAnalyzerConfigOptionsProvider(
            globalOptions: new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["build_property.RootNamespace"] = "GeneratorTests"
            },
            effectiveAdditionalFileOptions.Count == 0
                ? null
                : new Dictionary<string, IReadOnlyDictionary<string, string>>(StringComparer.Ordinal)
                {
                    [additionalText.Path] = effectiveAdditionalFileOptions
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
                && !s_knownGeneratorDiagnosticIds.Contains(diagnostic.Id))
            .ToArray();

        Assert.True(compilationErrors.Length == 0, string.Join(Environment.NewLine, compilationErrors.Select(static error => error.ToString())));

        var generatedSources = driver.GetRunResult()
            .Results
            .SelectMany(static result => result.GeneratedSources)
            .Select(static source => source.SourceText.ToString())
            .ToArray();

        return new GeneratorTestResult(generatorDiagnostics, generatedSources, outputCompilation);
    }

    private static IReadOnlyDictionary<string, string> CreateAdditionalFileOptions(
        IReadOnlyDictionary<string, string>? additionalFileOptions,
        bool isOpenApiWeaverDocument)
    {
        var options = new Dictionary<string, string>(StringComparer.Ordinal);

        if (isOpenApiWeaverDocument)
        {
            options[BuildMetadataAdditionalFilesItemKind] = OpenApiWeaverDocumentItemKind;
        }

        if (additionalFileOptions is not null)
        {
            foreach (var pair in additionalFileOptions)
            {
                options[pair.Key] = pair.Value;
            }
        }

        return options;
    }

    private static Diagnostic AssertSingleErrorDiagnostic(GeneratorTestResult result, string diagnosticId)
    {
        var diagnostic = Assert.Single(
            result.Diagnostics,
            diagnostic => string.Equals(diagnostic.Id, diagnosticId, StringComparison.Ordinal));

        Assert.Equal(DiagnosticSeverity.Error, diagnostic.Severity);
        return diagnostic;
    }

    private static Diagnostic AssertOnlyErrorDiagnostic(GeneratorTestResult result, string diagnosticId)
    {
        var diagnostic = Assert.Single(result.Diagnostics);

        Assert.Equal(diagnosticId, diagnostic.Id);
        Assert.Equal(DiagnosticSeverity.Error, diagnostic.Severity);
        return diagnostic;
    }

    private static void AssertNoErrorDiagnostics(GeneratorTestResult result)
    {
        Assert.DoesNotContain(result.Diagnostics, static diagnostic => diagnostic.Severity == DiagnosticSeverity.Error);
    }

    private static void AssertNoClientSource(GeneratorTestResult result)
    {
        Assert.DoesNotContain(result.GeneratedSources, static source => source.Contains(": IDisposable", StringComparison.Ordinal));
    }

    private static LoadedGeneratorAssembly LoadGeneratedAssembly(string openApi)
    {
        var result = RunGenerator(openApi);

        using var assemblyStream = new MemoryStream();
        var emitResult = result.Compilation.Emit(assemblyStream);
        Assert.True(
            emitResult.Success,
            string.Join(Environment.NewLine, emitResult.Diagnostics.Select(static diagnostic => diagnostic.ToString())));

        assemblyStream.Position = 0;

        var loadContext = new AssemblyLoadContext($"OpenApiWeaver.Tests.{Guid.NewGuid():N}", isCollectible: true);
        var assembly = loadContext.LoadFromStream(assemblyStream);
        return new LoadedGeneratorAssembly(loadContext, assembly);
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
        IReadOnlyList<string> generatedSources,
        Compilation compilation)
    {
        public IReadOnlyList<Diagnostic> Diagnostics { get; } = diagnostics;

        public IReadOnlyList<string> GeneratedSources { get; } = generatedSources;

        public Compilation Compilation { get; } = compilation;
    }

    private sealed class LoadedGeneratorAssembly(AssemblyLoadContext loadContext, Assembly assembly) : IDisposable
    {
        public Assembly Assembly { get; } = assembly;

        public void Dispose()
        {
            loadContext.Unload();
        }
    }
}
