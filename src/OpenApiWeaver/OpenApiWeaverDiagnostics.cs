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

    public static Diagnostic CreateDocumentEmpty(string path)
        => Diagnostic.Create(DocumentEmptyDescriptor, Location.None, path);

    public static Diagnostic CreateDocumentHasWarnings(string path, IEnumerable<string> warningMessages)
        => Diagnostic.Create(DocumentHasWarningsDescriptor, Location.None, path, string.Join(", ", warningMessages));

    public static Diagnostic CreateDocumentInvalid(string path, string message)
        => Diagnostic.Create(DocumentInvalidDescriptor, Location.None, path, message);

    public static Diagnostic CreateDocumentUnsupported(string path, string message)
        => Diagnostic.Create(DocumentUnsupportedDescriptor, Location.None, path, message);
}
