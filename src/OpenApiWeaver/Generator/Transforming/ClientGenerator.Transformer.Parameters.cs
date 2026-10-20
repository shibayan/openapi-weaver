using Microsoft.OpenApi;

using ModelParameterLocation = OpenApiWeaver.Models.ParameterLocation;
using OpenApiParameterLocation = Microsoft.OpenApi.ParameterLocation;

namespace OpenApiWeaver;

public sealed partial class ClientGenerator
{
    private sealed partial class Transformer
    {
        private static List<IOpenApiParameter> CollectParameters(IOpenApiPathItem pathItem, OpenApiOperation operation)
        {
            var pathParameters = pathItem.Parameters;
            var operationParameters = operation.Parameters;

            if ((pathParameters is null || pathParameters.Count == 0)
                && (operationParameters is null || operationParameters.Count == 0))
            {
                return [];
            }

            var parameters = new List<IOpenApiParameter>((pathParameters?.Count ?? 0) + (operationParameters?.Count ?? 0));
            var indices = new Dictionary<(OpenApiParameterLocation Location, string Name), int>();
            if (pathParameters is not null)
            {
                foreach (var parameter in pathParameters)
                {
                    AddOrReplaceParameter(parameters, indices, parameter);
                }
            }

            if (operationParameters is not null)
            {
                foreach (var parameter in operationParameters)
                {
                    AddOrReplaceParameter(parameters, indices, parameter);
                }
            }

            return parameters;
        }

        private static void AddOrReplaceParameter(
            List<IOpenApiParameter> parameters,
            Dictionary<(OpenApiParameterLocation Location, string Name), int> indices,
            IOpenApiParameter parameter)
        {
            var location = parameter.In ?? OpenApiParameterLocation.Query;
            if (IsReservedHeaderParameter(location, parameter.Name))
            {
                return;
            }

            var key = (
                location,
                location == OpenApiParameterLocation.Header
                    ? (parameter.Name ?? string.Empty).ToUpperInvariant()
                    : parameter.Name ?? string.Empty);
            if (indices.TryGetValue(key, out var index))
            {
                parameters[index] = parameter;
                return;
            }

            indices.Add(key, parameters.Count);
            parameters.Add(parameter);
        }

        private static string? GetTagName(OpenApiOperation operation)
        {
            return operation.Tags?.FirstOrDefault()?.Name;
        }

        private static bool IsReservedHeaderParameter(OpenApiParameterLocation location, string? name)
        {
            if (location != OpenApiParameterLocation.Header || string.IsNullOrEmpty(name))
            {
                return false;
            }

            return string.Equals(name, "Accept", StringComparison.OrdinalIgnoreCase)
                || string.Equals(name, "Content-Type", StringComparison.OrdinalIgnoreCase)
                || string.Equals(name, "Authorization", StringComparison.OrdinalIgnoreCase);
        }

        private IReadOnlyList<RequestBodyPropertyInfo>? ResolveParameterSerialization(IOpenApiParameter parameter, TypeUsage typeUsage)
        {
            ValidateParameterStyle(parameter, typeUsage);

            var location = parameter.In ?? OpenApiParameterLocation.Query;
            switch (typeUsage.Shape)
            {
                case TypeShape.Dictionary:
                    throw new UnsupportedGenerationException(
                        $"Parameter '{parameter.Name}' uses additionalProperties or patternProperties, which is not supported for compile-time code generation.",
                        UnsupportedFeatureKind.Parameter);
                case TypeShape.Object when location != OpenApiParameterLocation.Query || !parameter.Explode:
                    throw new UnsupportedGenerationException(
                        $"Parameter '{parameter.Name}' uses an object schema, which is only supported for query parameters with style: form and explode: true.",
                        UnsupportedFeatureKind.Parameter);
                case TypeShape.Object:
                    return ResolveQueryObjectProperties(parameter, typeUsage);
                case TypeShape.Array:
                    var itemShape = _schemaTypeResolver.ResolveTypeUsage(_schemaReferenceResolver.ResolveSchemaReference(parameter.Schema!).Items, required: true).Shape;
                    if (itemShape is TypeShape.Object or TypeShape.Dictionary or TypeShape.Array)
                    {
                        throw new UnsupportedGenerationException(
                            $"Parameter '{parameter.Name}' uses array items with an object or array schema, which is not supported for compile-time code generation.",
                            UnsupportedFeatureKind.Parameter);
                    }

                    return null;
                default:
                    return null;
            }
        }

        private List<RequestBodyPropertyInfo> ResolveQueryObjectProperties(IOpenApiParameter parameter, TypeUsage typeUsage)
        {
            var typeName = typeUsage.NonNullableCSharpTypeName;
            if (!_schemaDefinitionsByTypeName.TryGetValue(typeName, out var schemaDefinition)
                || schemaDefinition.IsPolymorphicBase
                || schemaDefinition.BaseTypeName is not null)
            {
                throw new UnsupportedGenerationException(
                    $"Parameter '{parameter.Name}' uses an object schema that cannot be expanded into query parameters for compile-time code generation.",
                    UnsupportedFeatureKind.Parameter);
            }

            var context = new FormValueContext(
                $"Query parameter '{parameter.Name}' schema '{typeName}'",
                allowsBinary: false,
                "query parameters",
                UnsupportedFeatureKind.Parameter);
            return CreateFormProperties(context, typeName, _schemaReferenceResolver.ResolveSchemaReference(parameter.Schema!));
        }

        private static void ValidateParameterStyle(IOpenApiParameter parameter, TypeUsage typeUsage)
        {
            var location = parameter.In ?? OpenApiParameterLocation.Query;
            var style = parameter.Style;
            var supportedStyle = location switch
            {
                OpenApiParameterLocation.Query or OpenApiParameterLocation.Cookie => style is null or ParameterStyle.Form,
                _ => style is null or ParameterStyle.Simple,
            };

            if (!supportedStyle)
            {
                throw new UnsupportedGenerationException(
                    $"Parameter '{parameter.Name}' uses style '{style}', which is not supported for compile-time code generation.",
                    UnsupportedFeatureKind.Parameter);
            }

            if (location == OpenApiParameterLocation.Query
                && typeUsage.Shape == TypeShape.Array
                && !parameter.Explode)
            {
                throw new UnsupportedGenerationException(
                    $"Parameter '{parameter.Name}' uses explode: false with an array schema, which is not supported for compile-time code generation.",
                    UnsupportedFeatureKind.Parameter);
            }
        }

        private static ModelParameterLocation MapParameterLocation(OpenApiParameterLocation location)
        {
            return location switch
            {
                OpenApiParameterLocation.Path => ModelParameterLocation.Path,
                OpenApiParameterLocation.Header => ModelParameterLocation.Header,
                OpenApiParameterLocation.Cookie => ModelParameterLocation.Cookie,
                _ => ModelParameterLocation.Query,
            };
        }
    }
}
