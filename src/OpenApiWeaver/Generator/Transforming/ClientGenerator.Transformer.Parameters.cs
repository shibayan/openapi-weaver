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
                case TypeShape.JsonElement when SerializesAsCompositeValue(parameter.Schema!, new HashSet<string>(StringComparer.Ordinal)):
                    // An object or array without a generated model would otherwise be formatted as its raw JSON text.
                    throw new UnsupportedGenerationException(
                        $"Parameter '{parameter.Name}' uses an object or array schema that has no generated model, which is not supported for compile-time code generation.",
                        UnsupportedFeatureKind.Parameter);
                case TypeShape.Array:
                    if (FindArrayItemsSchema(parameter.Schema!, new HashSet<string>(StringComparer.Ordinal)) is { } itemsSchema)
                    {
                        var itemShape = _schemaTypeResolver.ResolveTypeUsage(itemsSchema, required: true).Shape;
                        if (itemShape is TypeShape.Object or TypeShape.Dictionary or TypeShape.Array
                            || (itemShape == TypeShape.JsonElement && SerializesAsCompositeValue(itemsSchema, new HashSet<string>(StringComparer.Ordinal))))
                        {
                            throw new UnsupportedGenerationException(
                                $"Parameter '{parameter.Name}' uses array items with an object or array schema, which is not supported for compile-time code generation.",
                                UnsupportedFeatureKind.Parameter);
                        }
                    }

                    return null;
                default:
                    return null;
            }
        }

        // Whether a value of this schema can be an object or an array on the wire, including through allOf / oneOf / anyOf
        // members. Such a value has no scalar form, so a JsonElement fallback would be sent as its raw JSON text.
        // Kept separate from ComposesObject, which decides whether a model is generated.
        private static bool SerializesAsCompositeValue(IOpenApiSchema schema, HashSet<string> visited)
        {
            if (!visited.Add(SchemaReferenceResolver.GetSchemaIdentity(schema)))
            {
                return false;
            }

            if ((schema.Type & JsonSchemaType.Array) == JsonSchemaType.Array
                || ComposesObject(schema, new HashSet<string>(StringComparer.Ordinal)))
            {
                return true;
            }

            // allOf members are walked too, so a union wrapped in allOf (e.g. `allOf: [{ anyOf: [object, string] }]`) is found.
            return (schema.AllOf ?? []).Concat(schema.OneOf ?? []).Concat(schema.AnyOf ?? [])
                .Any(member => !SchemaReferenceResolver.IsNullOnlySchema(member) && SerializesAsCompositeValue(member, visited));
        }

        // Finds the items schema of the array a parameter resolves to, including arrays supplied through composition.
        private IOpenApiSchema? FindArrayItemsSchema(IOpenApiSchema schema, HashSet<string> visited)
        {
            var resolvedSchema = _schemaReferenceResolver.ResolveSchemaReference(schema);
            if (!visited.Add(SchemaReferenceResolver.GetSchemaIdentity(resolvedSchema)))
            {
                return null;
            }

            if (resolvedSchema.Items is not null)
            {
                return resolvedSchema.Items;
            }

            var members = (resolvedSchema.AllOf ?? []).Concat(resolvedSchema.OneOf ?? []).Concat(resolvedSchema.AnyOf ?? []);
            foreach (var member in members)
            {
                if (!SchemaReferenceResolver.IsNullOnlySchema(member) && FindArrayItemsSchema(member, visited) is { } itemsSchema)
                {
                    return itemsSchema;
                }
            }

            return null;
        }

        // Whether the schema, or anything it composes through allOf, has non-null oneOf / anyOf alternatives.
        private static bool HasUnionMembers(IOpenApiSchema schema, HashSet<string> visited)
        {
            if (!visited.Add(SchemaReferenceResolver.GetSchemaIdentity(schema)))
            {
                return false;
            }

            return (schema.OneOf ?? []).Concat(schema.AnyOf ?? []).Any(static member => !SchemaReferenceResolver.IsNullOnlySchema(member))
                || (schema.AllOf ?? []).Any(member => HasUnionMembers(member, visited));
        }

        private List<RequestBodyPropertyInfo> ResolveQueryObjectProperties(IOpenApiParameter parameter, TypeUsage typeUsage)
        {
            var typeName = typeUsage.NonNullableCSharpTypeName;
            if (!_schemaDefinitionsByTypeName.TryGetValue(typeName, out var schemaDefinition)
                || !_schemaSourcesByTypeName.TryGetValue(typeName, out var objectSchema)
                || schemaDefinition.IsPolymorphicBase
                || schemaDefinition.BaseTypeName is not null)
            {
                throw new UnsupportedGenerationException(
                    $"Parameter '{parameter.Name}' uses an object schema that cannot be expanded into query parameters for compile-time code generation.",
                    UnsupportedFeatureKind.Parameter);
            }

            // Property collection ignores oneOf/anyOf branches, so expanding such a model would silently drop
            // branch-specific fields; the form-body path rejects these unions for the same reason.
            if (HasUnionMembers(objectSchema, new HashSet<string>(StringComparer.Ordinal)))
            {
                throw new UnsupportedGenerationException(
                    $"Parameter '{parameter.Name}' schema '{typeName}' uses oneOf/anyOf, which cannot be expanded into query parameters for compile-time code generation.",
                    UnsupportedFeatureKind.Parameter);
            }

            var context = new FormValueContext(
                $"Query parameter '{parameter.Name}' schema '{typeName}'",
                allowsBinary: false,
                "query parameters",
                UnsupportedFeatureKind.Parameter);

            // Collect from the model's own schema so wrappers such as `anyOf: [$ref: Filter, null]` expand like Filter itself.
            return CreateFormProperties(context, typeName, objectSchema);
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
