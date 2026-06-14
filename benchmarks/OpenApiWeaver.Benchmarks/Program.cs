using System.Collections.Immutable;
using System.Text;

using BenchmarkDotNet.Attributes;
using BenchmarkDotNet.Running;

using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Diagnostics;
using Microsoft.CodeAnalysis.Text;

using OpenApiWeaver;

BenchmarkSwitcher.FromAssembly(typeof(ClientGeneratorBenchmarks).Assembly).Run(args);

public enum OpenApiBenchmarkDocument
{
    ApiSchemaJson,
    PetstoreYaml
}

[MemoryDiagnoser]
public class ClientGeneratorBenchmarks
{
    private static readonly ImmutableArray<MetadataReference> s_metadataReferences = [..
        ((string?)AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES"))!
            .Split(Path.PathSeparator)
            .Distinct()
            .Select(static path => (MetadataReference)MetadataReference.CreateFromFile(path))];

    private readonly CSharpParseOptions _parseOptions = new(LanguageVersion.Preview);
    private CSharpCompilation _compilation = null!;
    private InMemoryAdditionalText _additionalText = null!;
    private TestAnalyzerConfigOptionsProvider _optionsProvider = null!;
    private GeneratorDriver _warmDriver = null!;

    [Params(OpenApiBenchmarkDocument.ApiSchemaJson, OpenApiBenchmarkDocument.PetstoreYaml)]
    public OpenApiBenchmarkDocument Document { get; set; }

    [GlobalSetup]
    public void Setup()
    {
        var fileName = Document switch
        {
            OpenApiBenchmarkDocument.ApiSchemaJson => "api-schema.json",
            OpenApiBenchmarkDocument.PetstoreYaml => "petstore.yaml",
            _ => throw new ArgumentOutOfRangeException(nameof(Document))
        };

        var path = Path.Combine(AppContext.BaseDirectory, "openapi", fileName);
        var content = File.ReadAllText(path);
        var syntaxTree = CSharpSyntaxTree.ParseText("public sealed class Marker {}", _parseOptions);

        _compilation = CSharpCompilation.Create(
            "OpenApiWeaver.Benchmarks.Target",
            [syntaxTree],
            s_metadataReferences,
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));

        _additionalText = new InMemoryAdditionalText(fileName, content);
        _optionsProvider = new TestAnalyzerConfigOptionsProvider(
            globalOptions: new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["build_property.RootNamespace"] = "OpenApiWeaver.Benchmarks.Target"
            },
            additionalFileOptions: new Dictionary<string, IReadOnlyDictionary<string, string>>(StringComparer.Ordinal)
            {
                [_additionalText.Path] = new Dictionary<string, string>(StringComparer.Ordinal)
                {
                    ["build_metadata.AdditionalFiles.OpenApiWeaverItemKind"] = "Document"
                }
            });

        _warmDriver = CreateDriver().RunGenerators(_compilation);
    }

    [Benchmark(Baseline = true)]
    public GeneratorDriverRunResult ColdRun()
    {
        var driver = CreateDriver().RunGenerators(_compilation);
        return driver.GetRunResult();
    }

    [Benchmark]
    public GeneratorDriverRunResult IncrementalNoChanges()
    {
        var driver = _warmDriver.RunGenerators(_compilation);
        return driver.GetRunResult();
    }

    private GeneratorDriver CreateDriver()
    {
        var generator = new ClientGenerator();
        return CSharpGeneratorDriver.Create(
            generators: [generator.AsSourceGenerator()],
            additionalTexts: [_additionalText],
            parseOptions: _parseOptions,
            optionsProvider: _optionsProvider);
    }

    private sealed class InMemoryAdditionalText(string path, string content) : AdditionalText
    {
        public override string Path { get; } = path;

        public override SourceText GetText(CancellationToken cancellationToken = default)
            => SourceText.From(content, Encoding.UTF8);
    }

    private sealed class TestAnalyzerConfigOptionsProvider(
        IReadOnlyDictionary<string, string> globalOptions,
        IReadOnlyDictionary<string, IReadOnlyDictionary<string, string>> additionalFileOptions) : AnalyzerConfigOptionsProvider
    {
        public override AnalyzerConfigOptions GlobalOptions { get; } = new DictionaryAnalyzerConfigOptions(globalOptions);

        public override AnalyzerConfigOptions GetOptions(SyntaxTree tree)
            => DictionaryAnalyzerConfigOptions.Empty;

        public override AnalyzerConfigOptions GetOptions(AdditionalText textFile)
            => additionalFileOptions.TryGetValue(textFile.Path, out var options)
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
}
