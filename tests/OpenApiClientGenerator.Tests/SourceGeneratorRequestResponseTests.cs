using System.Collections.Immutable;

using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Text;

using Xunit;

namespace OpenApiClientGenerator.Tests;

public sealed partial class SourceGeneratorRequestResponseTests
{
    private static string GenerateSource(string openApi)
    {
        var result = RunGenerator(openApi);
        Assert.DoesNotContain(result.Diagnostics, static diagnostic => diagnostic.Severity == DiagnosticSeverity.Error);
        return Assert.Single(result.GeneratedSources);
    }

    private static readonly ImmutableArray<MetadataReference> s_metadataReferences = [..
        ((string?)AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES"))!
            .Split(Path.PathSeparator)
            .Distinct()
            .Select(static path => (MetadataReference)MetadataReference.CreateFromFile(path))];

    private static GeneratorTestResult RunGenerator(string openApi)
    {
        var generator = new OpenApiClientSourceGenerator();
        var parseOptions = new CSharpParseOptions(LanguageVersion.Preview);
        var syntaxTree = CSharpSyntaxTree.ParseText("public sealed class Marker {}", parseOptions);

        var compilation = CSharpCompilation.Create(
            "GeneratorTests",
            [syntaxTree],
            s_metadataReferences,
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));

        var additionalText = new InMemoryAdditionalText("test.yaml", openApi);
        GeneratorDriver driver = CSharpGeneratorDriver.Create(
            generators: [generator.AsSourceGenerator()],
            additionalTexts: [additionalText],
            parseOptions: parseOptions);

        driver = driver.RunGeneratorsAndUpdateCompilation(compilation, out var outputCompilation, out var diagnostics);

        var generatorDiagnostics = driver.GetRunResult()
            .Results
            .SelectMany(static result => result.Diagnostics)
            .ToArray();

        var compilationErrors = diagnostics
            .Concat(outputCompilation.GetDiagnostics())
            .Where(static diagnostic => diagnostic.Severity == DiagnosticSeverity.Error
                && diagnostic.Id != "OARSG002"
                && diagnostic.Id != "OARSG004")
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

    private sealed class GeneratorTestResult(
        IReadOnlyList<Diagnostic> diagnostics,
        IReadOnlyList<string> generatedSources)
    {
        public IReadOnlyList<Diagnostic> Diagnostics { get; } = diagnostics;

        public IReadOnlyList<string> GeneratedSources { get; } = generatedSources;
    }
}
