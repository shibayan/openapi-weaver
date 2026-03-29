using System.Text;

using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Diagnostics;
using Microsoft.CodeAnalysis.Text;
using Microsoft.OpenApi.Reader;
using Microsoft.OpenApi.YamlReader;

namespace OpenApiWeaver;

[Generator]
public sealed partial class OpenApiWeaverSourceGenerator : IIncrementalGenerator
{
    public void Initialize(IncrementalGeneratorInitializationContext context)
    {
        var openApiFiles = context.AdditionalTextsProvider
            .Where(static file => IsOpenApiDocument(file.Path))
            .Combine(context.AnalyzerConfigOptionsProvider)
            .Select(static (pair, _) => CreateInput(pair.Left, pair.Right));

        context.RegisterSourceOutput(openApiFiles, static (productionContext, input) =>
        {
            var file = input.File;
            var sourceText = file.GetText(productionContext.CancellationToken);
            var content = sourceText?.ToString();
            if (string.IsNullOrWhiteSpace(content))
            {
                productionContext.ReportDiagnostic(Diagnostic.Create(Diagnostics.DocumentEmpty, Location.None, file.Path));
                return;
            }

            ReadResult readResult;
            try
            {
                using var stream = new MemoryStream(Encoding.UTF8.GetBytes(content), writable: false);
                readResult = ReadOpenApiDocument(file.Path, stream);
            }
            catch (Exception exception)
            {
                productionContext.ReportDiagnostic(Diagnostic.Create(
                    Diagnostics.DocumentInvalid,
                    Location.None,
                    file.Path,
                    exception.Message));
                return;
            }

            var document = readResult.Document;
            var diagnostic = readResult.Diagnostic;
            if (document?.Paths is null)
            {
                productionContext.ReportDiagnostic(Diagnostic.Create(
                    Diagnostics.DocumentInvalid,
                    Location.None,
                    file.Path,
                    "The document could not be loaded."));
                return;
            }

            if (diagnostic?.Errors.Count > 0)
            {
                productionContext.ReportDiagnostic(Diagnostic.Create(
                    Diagnostics.DocumentHasWarnings,
                    Location.None,
                    file.Path,
                    string.Join(", ", diagnostic.Errors.Select(static error => error.Message))));
            }

            try
            {
                var source = new ClientEmitter(file.Path, input.RootNamespace, input.Namespace, input.ClientName, document).Emit();
                productionContext.AddSource($"{SanitizeHintName(file.Path)}.g.cs", SourceText.From(source, Encoding.UTF8));
            }
            catch (UnsupportedGenerationException exception)
            {
                productionContext.ReportDiagnostic(Diagnostic.Create(
                    Diagnostics.DocumentUnsupported,
                    Location.None,
                    file.Path,
                    exception.Message));
            }
            catch (Exception exception)
            {
                productionContext.ReportDiagnostic(Diagnostic.Create(
                    Diagnostics.DocumentInvalid,
                    Location.None,
                    file.Path,
                    exception.Message));
            }
        });
    }

    private static bool IsOpenApiDocument(string path)
    {
        var extension = Path.GetExtension(path);
        return string.Equals(extension, ".yaml", StringComparison.OrdinalIgnoreCase)
            || string.Equals(extension, ".yml", StringComparison.OrdinalIgnoreCase)
            || string.Equals(extension, ".json", StringComparison.OrdinalIgnoreCase);
    }

    private static GeneratorInput CreateInput(AdditionalText file, AnalyzerConfigOptionsProvider optionsProvider)
    {
        optionsProvider.GlobalOptions.TryGetValue("build_property.RootNamespace", out var rootNamespace);

        var fileOptions = optionsProvider.GetOptions(file);
        fileOptions.TryGetValue("build_metadata.AdditionalFiles.Namespace", out var configuredNamespace);
        fileOptions.TryGetValue("build_metadata.AdditionalFiles.ClientName", out var clientName);

        return new GeneratorInput(
            file,
            rootNamespace ?? string.Empty,
            NormalizeOption(configuredNamespace),
            NormalizeOption(clientName));
    }

    private static string? NormalizeOption(string? value)
        => string.IsNullOrWhiteSpace(value) ? null : value;

    private static ReadResult ReadOpenApiDocument(string path, MemoryStream stream)
    {
        var location = CreateDocumentUri(path);
        var settings = new OpenApiReaderSettings();

        if (IsYamlDocument(path))
        {
            settings.AddYamlReader();
            return new OpenApiYamlReader().Read(stream, location, settings);
        }

        settings.AddJsonReader();
        return new OpenApiJsonReader().Read(stream, location, settings);
    }

    private static bool IsYamlDocument(string path)
    {
        var extension = Path.GetExtension(path);
        return string.Equals(extension, ".yaml", StringComparison.OrdinalIgnoreCase)
            || string.Equals(extension, ".yml", StringComparison.OrdinalIgnoreCase);
    }

    private static Uri CreateDocumentUri(string path)
    {
        return Uri.TryCreate(path, UriKind.Absolute, out var absoluteUri)
            ? absoluteUri
            : new Uri(Path.GetFullPath(path));
    }

    private static string SanitizeHintName(string value)
        => value.Replace('<', '_').Replace('>', '_').Replace('.', '_').Replace('\\', '_').Replace('/', '_').Replace(':', '_');

    private static class Diagnostics
    {
        public static readonly DiagnosticDescriptor DocumentEmpty = new(
            "OAW001",
            "OpenAPI document is empty",
            "The OpenAPI document '{0}' is empty",
            "OpenApiWeaver",
            DiagnosticSeverity.Error,
            isEnabledByDefault: true);

        public static readonly DiagnosticDescriptor DocumentHasWarnings = new(
            "OAW002",
            "OpenAPI document has validation warnings",
            "The OpenAPI document '{0}' was loaded with validation warnings: {1}",
            "OpenApiWeaver",
            DiagnosticSeverity.Warning,
            isEnabledByDefault: true);

        public static readonly DiagnosticDescriptor DocumentInvalid = new(
            "OAW003",
            "OpenAPI document is invalid",
            "The OpenAPI document '{0}' could not be parsed: {1}",
            "OpenApiWeaver",
            DiagnosticSeverity.Error,
            isEnabledByDefault: true);

        public static readonly DiagnosticDescriptor DocumentUnsupported = new(
            "OAW004",
            "OpenAPI document uses an unsupported feature",
            "The OpenAPI document '{0}' uses an unsupported feature: {1}",
            "OpenApiWeaver",
            DiagnosticSeverity.Error,
            isEnabledByDefault: true);
    }

    private sealed class GeneratorInput
    {
        public GeneratorInput(AdditionalText file, string rootNamespace, string? @namespace, string? clientName)
        {
            File = file;
            RootNamespace = rootNamespace;
            Namespace = @namespace;
            ClientName = clientName;
        }

        public AdditionalText File { get; }

        public string RootNamespace { get; }

        public string? Namespace { get; }

        public string? ClientName { get; }
    }

    private sealed class UnsupportedGenerationException(string message) : Exception(message);
}
