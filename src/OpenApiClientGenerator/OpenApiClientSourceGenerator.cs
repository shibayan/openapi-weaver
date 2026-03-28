using System.Text;

using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Text;
using Microsoft.OpenApi;
using Microsoft.OpenApi.Reader;
using Microsoft.OpenApi.YamlReader;

namespace OpenApiClientGenerator;

[Generator]
public sealed partial class OpenApiClientSourceGenerator : IIncrementalGenerator
{
    public void Initialize(IncrementalGeneratorInitializationContext context)
    {
        var rootNamespace = context.AnalyzerConfigOptionsProvider
            .Select(static (provider, _) =>
            {
                provider.GlobalOptions.TryGetValue("build_property.RootNamespace", out var value);
                return value ?? string.Empty;
            });

        var openApiFiles = context.AdditionalTextsProvider
            .Where(static file => IsOpenApiDocument(file.Path));

        context.RegisterSourceOutput(openApiFiles.Combine(rootNamespace), static (productionContext, pair) =>
        {
            var file = pair.Left;
            var configuredRootNamespace = pair.Right;
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

            var source = new ClientEmitter(file.Path, configuredRootNamespace, document).Emit();
            productionContext.AddSource($"{SanitizeHintName(file.Path)}.g.cs", SourceText.From(source, Encoding.UTF8));
        });
    }

    private static bool IsOpenApiDocument(string path)
    {
        var extension = Path.GetExtension(path);
        return string.Equals(extension, ".yaml", StringComparison.OrdinalIgnoreCase)
            || string.Equals(extension, ".yml", StringComparison.OrdinalIgnoreCase)
            || string.Equals(extension, ".json", StringComparison.OrdinalIgnoreCase);
    }

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
            "OARSG002",
            "OpenAPI document is empty",
            "The OpenAPI document '{0}' is empty",
            "OpenApiClientGenerator",
            DiagnosticSeverity.Error,
            isEnabledByDefault: true);

        public static readonly DiagnosticDescriptor DocumentHasWarnings = new(
            "OARSG003",
            "OpenAPI document has validation warnings",
            "The OpenAPI document '{0}' was loaded with validation warnings: {1}",
            "OpenApiClientGenerator",
            DiagnosticSeverity.Warning,
            isEnabledByDefault: true);

        public static readonly DiagnosticDescriptor DocumentInvalid = new(
            "OARSG004",
            "OpenAPI document is invalid",
            "The OpenAPI document '{0}' could not be parsed: {1}",
            "OpenApiClientGenerator",
            DiagnosticSeverity.Error,
            isEnabledByDefault: true);
    }
}
