using Microsoft.OpenApi;

using OpenApiParameterLocation = Microsoft.OpenApi.ParameterLocation;

namespace OpenApiWeaver;

public sealed partial class ClientGenerator
{
    private sealed partial class Transformer
    {
        private OperationGroupItem BuildOperation(
            string route,
            string operationType,
            IOpenApiPathItem pathItem,
            OpenApiOperation operation,
            ISet<string> usedMethodNames,
            IReadOnlyDictionary<string, SecuritySchemeBinding> securitySchemesByKey)
        {
            var tagName = GetTagName(operation);
            var usedParameterNames = new HashSet<string>(StringComparer.Ordinal)
            {
                "cancellationToken"
            };
            var parameters = CollectParameters(pathItem, operation)
                .Select(parameter => new ParameterInfo(
                    parameter.Name ?? string.Empty,
                    AllocateUniqueName(
                        usedParameterNames,
                        NormalizeCamelIdentifier(parameter.Name ?? string.Empty, "value"),
                        "value"),
                    _schemaTypeResolver.ResolveTypeUsage(parameter.Schema, parameter.Required),
                    parameter.Required,
                    MapParameterLocation(parameter.In ?? OpenApiParameterLocation.Query),
                    parameter.Description))
                .ToList();

            var requestBody = ResolveRequestBody(operation.RequestBody, usedParameterNames);
            var response = ResolveResponse(operation);
            var errorResponses = ResolveErrorResponses(operation);
            var operationSecurityRequirements = ResolveOperationSecurityRequirements(operation, securitySchemesByKey);
            var methodName = AllocateUniqueName(
                usedMethodNames,
                BuildOperationMethodName(operation.OperationId, operationType, route, tagName),
                NormalizePascalIdentifier(operationType, "Operation"));

            return new OperationGroupItem(
                route,
                operationType,
                methodName,
                operation.Summary ?? operation.Description ?? $"{CSharpUtilities.ToPascalCase(operationType.ToLowerInvariant())} {route}.",
                operation.Summary is not null && !string.IsNullOrWhiteSpace(operation.Description) ? operation.Description : null,
                parameters,
                requestBody,
                response,
                errorResponses,
                operationSecurityRequirements);
        }
    }
}
