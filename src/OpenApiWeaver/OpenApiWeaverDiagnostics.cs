using Microsoft.CodeAnalysis;

namespace OpenApiWeaver;

internal static class OpenApiWeaverDiagnostics
{
    private const string Category = "OpenApiWeaver";

    public static readonly DiagnosticDescriptor DocumentEmptyDescriptor = new(
        "OAW001",
        "OpenAPI document is empty",
        "The OpenAPI document '{0}' is empty or contains only whitespace",
        Category,
        DiagnosticSeverity.Error,
        isEnabledByDefault: true);

    public static readonly DiagnosticDescriptor DocumentHasWarningsDescriptor = new(
        "OAW002",
        "OpenAPI document has validation warnings",
        "The OpenAPI document '{0}' contains validation warnings: {1}",
        Category,
        DiagnosticSeverity.Warning,
        isEnabledByDefault: true);

    public static readonly DiagnosticDescriptor DocumentInvalidDescriptor = new(
        "OAW003",
        "OpenAPI document is invalid",
        "The OpenAPI document '{0}' could not be parsed: {1}",
        Category,
        DiagnosticSeverity.Error,
        isEnabledByDefault: true);

    public static readonly DiagnosticDescriptor DocumentUnsupportedDescriptor = new(
        "OAW004",
        "OpenAPI document uses an unsupported feature",
        "The OpenAPI document '{0}' uses an unsupported feature: {1}",
        Category,
        DiagnosticSeverity.Error,
        isEnabledByDefault: true);

    public static readonly DiagnosticDescriptor RequestBodyUnsupportedDescriptor = new(
        "OAW005",
        "OpenAPI request body uses an unsupported feature",
        "The OpenAPI document '{0}' has an unsupported request body: {1}",
        Category,
        DiagnosticSeverity.Error,
        isEnabledByDefault: true);

    public static readonly DiagnosticDescriptor DiscriminatorUnsupportedDescriptor = new(
        "OAW006",
        "OpenAPI discriminator uses an unsupported feature",
        "The OpenAPI document '{0}' has an unsupported discriminator: {1}",
        Category,
        DiagnosticSeverity.Error,
        isEnabledByDefault: true);

    public static readonly DiagnosticDescriptor SchemaUnsupportedDescriptor = new(
        "OAW007",
        "OpenAPI schema uses an unsupported feature",
        "The OpenAPI document '{0}' has an unsupported schema: {1}",
        Category,
        DiagnosticSeverity.Error,
        isEnabledByDefault: true);

    public static Diagnostic CreateDocumentEmpty(string path)
        => Diagnostic.Create(DocumentEmptyDescriptor, Location.None, path);

    public static Diagnostic CreateDocumentHasWarnings(string path, IEnumerable<string> warningMessages)
        => Diagnostic.Create(DocumentHasWarningsDescriptor, Location.None, path, string.Join(", ", warningMessages));

    public static Diagnostic CreateDocumentInvalid(string path, string message)
        => Diagnostic.Create(DocumentInvalidDescriptor, Location.None, path, message);

    public static Diagnostic CreateDocumentUnsupported(string path, string message)
        => Diagnostic.Create(DocumentUnsupportedDescriptor, Location.None, path, message);

    public static Diagnostic CreateDocumentUnsupported(string path, UnsupportedFeatureKind kind, string message)
        => Diagnostic.Create(GetUnsupportedDescriptor(kind), Location.None, path, message);

    private static DiagnosticDescriptor GetUnsupportedDescriptor(UnsupportedFeatureKind kind)
        => kind switch
        {
            UnsupportedFeatureKind.RequestBody => RequestBodyUnsupportedDescriptor,
            UnsupportedFeatureKind.Discriminator => DiscriminatorUnsupportedDescriptor,
            UnsupportedFeatureKind.Schema => SchemaUnsupportedDescriptor,
            _ => DocumentUnsupportedDescriptor
        };
}

internal enum UnsupportedFeatureKind
{
    General,
    RequestBody,
    Discriminator,
    Schema
}
