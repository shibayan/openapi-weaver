using Microsoft.CodeAnalysis;

namespace OpenApiWeaver;

public sealed partial class ClientGenerator
{
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
}
