using System.Text;

using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Diagnostics;
using Microsoft.CodeAnalysis.Text;
using Microsoft.OpenApi.Reader;
using Microsoft.OpenApi.YamlReader;

namespace OpenApiWeaver;

[Generator]
public sealed partial class ClientGenerator : IIncrementalGenerator
{
    public void Initialize(IncrementalGeneratorInitializationContext context)
    {
        var openApiFiles = context.AdditionalTextsProvider
            .Where(static file => IsOpenApiDocument(file.Path))
            .Combine(context.AnalyzerConfigOptionsProvider)
            .Select(static (pair, cancellationToken) => TryCreateInput(pair.Left, pair.Right, cancellationToken))
            .Where(static input => input is not null);

        context.RegisterSourceOutput(openApiFiles, static (productionContext, input) =>
        {
            EmitClientSource(productionContext, input!);
        });

        var namespaces = openApiFiles
            .Select(static (input, _) => input!.RootNamespace)
            .Collect()
            .SelectMany(static (items, _) => items.Distinct(StringComparer.Ordinal));

        context.RegisterSourceOutput(namespaces, static (productionContext, rootNamespace) =>
        {
            var source = SupportTypesEmitter.Emit(rootNamespace);
            var hintName = string.IsNullOrEmpty(rootNamespace)
                ? "OpenApiWeaver.Support.g.cs"
                : $"{SanitizeHintName(rootNamespace)}.Support.g.cs";
            productionContext.AddSource(hintName, SourceText.From(source, Encoding.UTF8));
        });
    }

    private static void EmitClientSource(SourceProductionContext productionContext, GeneratorInput input)
    {
        if (string.IsNullOrWhiteSpace(input.Content))
        {
            productionContext.ReportDiagnostic(Diagnostic.Create(Diagnostics.DocumentEmpty, Location.None, input.Path));
            return;
        }

        ReadResult readResult;
        try
        {
            using var stream = new MemoryStream(Encoding.UTF8.GetBytes(input.Content), writable: false);
            readResult = ReadOpenApiDocument(input.Path, stream);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception exception)
        {
            productionContext.ReportDiagnostic(Diagnostic.Create(
                Diagnostics.DocumentInvalid,
                Location.None,
                input.Path,
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
                input.Path,
                "The document does not contain valid paths or could not be loaded."));
            return;
        }

        if (diagnostic?.Errors.Count > 0)
        {
            productionContext.ReportDiagnostic(Diagnostic.Create(
                Diagnostics.DocumentHasWarnings,
                Location.None,
                input.Path,
                string.Join(", ", diagnostic.Errors.Select(static error => error.Message))));
        }

        try
        {
            var model = new Transformer(input.Path, input.RootNamespace, input.ClientName, document).Transform();
            var source = new Emitter(model).Emit();
            productionContext.AddSource($"{SanitizeHintName(input.Path)}.g.cs", SourceText.From(source, Encoding.UTF8));
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (UnsupportedGenerationException exception)
        {
            productionContext.ReportDiagnostic(Diagnostic.Create(
                Diagnostics.DocumentUnsupported,
                Location.None,
                input.Path,
                exception.Message));
        }
        catch (Exception exception)
        {
            productionContext.ReportDiagnostic(Diagnostic.Create(
                Diagnostics.DocumentInvalid,
                Location.None,
                input.Path,
                exception.Message));
        }
    }

    private static bool IsOpenApiDocument(string path)
    {
        var extension = Path.GetExtension(path);
        return string.Equals(extension, ".yaml", StringComparison.OrdinalIgnoreCase)
            || string.Equals(extension, ".yml", StringComparison.OrdinalIgnoreCase)
            || string.Equals(extension, ".json", StringComparison.OrdinalIgnoreCase);
    }

    private static GeneratorInput? TryCreateInput(AdditionalText file, AnalyzerConfigOptionsProvider optionsProvider, CancellationToken cancellationToken)
    {
        var fileOptions = optionsProvider.GetOptions(file);
        if (!fileOptions.TryGetValue(BuildMetadataAdditionalFilesItemKind, out var itemKind)
            || !string.Equals(itemKind, OpenApiWeaverDocumentItemKind, StringComparison.Ordinal))
        {
            return null;
        }

        optionsProvider.GlobalOptions.TryGetValue(BuildPropertyRootNamespace, out var rootNamespace);
        fileOptions.TryGetValue(BuildMetadataAdditionalFilesNamespace, out var configuredNamespace);
        fileOptions.TryGetValue(BuildMetadataAdditionalFilesClientName, out var clientName);

        var content = file.GetText(cancellationToken)?.ToString() ?? string.Empty;

        var effectiveNamespace = NormalizeOption(configuredNamespace) ?? rootNamespace ?? string.Empty;

        return new GeneratorInput(
            file.Path,
            content,
            effectiveNamespace,
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

    private const string BuildPropertyRootNamespace = "build_property.RootNamespace";
    private const string BuildMetadataAdditionalFilesClientName = "build_metadata.AdditionalFiles.ClientName";
    private const string BuildMetadataAdditionalFilesNamespace = "build_metadata.AdditionalFiles.Namespace";
    private const string BuildMetadataAdditionalFilesItemKind = "build_metadata.AdditionalFiles.OpenApiWeaverItemKind";
    private const string OpenApiWeaverDocumentItemKind = "Document";

    private static class Diagnostics
    {
        public static readonly DiagnosticDescriptor DocumentEmpty = new(
            "OAW001",
            "OpenAPI document is empty",
            "The OpenAPI document '{0}' is empty or contains only whitespace",
            "OpenApiWeaver",
            DiagnosticSeverity.Error,
            isEnabledByDefault: true);

        public static readonly DiagnosticDescriptor DocumentHasWarnings = new(
            "OAW002",
            "OpenAPI document has validation warnings",
            "The OpenAPI document '{0}' contains validation warnings: {1}",
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

    private sealed class GeneratorInput(string path, string content, string rootNamespace, string? clientName) : IEquatable<GeneratorInput>
    {
        public string Path { get; } = path;

        public string Content { get; } = content;

        public string RootNamespace { get; } = rootNamespace;

        public string? ClientName { get; } = clientName;

        public bool Equals(GeneratorInput? other)
        {
            if (other is null)
            {
                return false;
            }

            if (ReferenceEquals(this, other))
            {
                return true;
            }

            return string.Equals(Path, other.Path, StringComparison.Ordinal)
                && string.Equals(Content, other.Content, StringComparison.Ordinal)
                && string.Equals(RootNamespace, other.RootNamespace, StringComparison.Ordinal)
                && string.Equals(ClientName, other.ClientName, StringComparison.Ordinal);
        }

        public override bool Equals(object? obj) => Equals(obj as GeneratorInput);

        public override int GetHashCode()
        {
            unchecked
            {
                var hash = Path.GetHashCode();
                hash = (hash * 397) ^ Content.GetHashCode();
                hash = (hash * 397) ^ RootNamespace.GetHashCode();
                hash = (hash * 397) ^ (ClientName?.GetHashCode() ?? 0);
                return hash;
            }
        }
    }

    private sealed class UnsupportedGenerationException(string message) : Exception(message);
}
