using Microsoft.OpenApi;

namespace OpenApiWeaver;

public sealed partial class OpenApiWeaverSourceGenerator
{
    private sealed partial class DocumentTransformer
    {
        private RequestBodyInfo? ResolveRequestBody(IOpenApiRequestBody? requestBody)
        {
            if (requestBody?.Content is null || requestBody.Content.Count == 0)
            {
                return null;
            }

            var selectedContent = SelectPreferredContent(
                requestBody.Content,
                static item => string.Equals(item.Key, "application/json", StringComparison.OrdinalIgnoreCase) ? 0 :
                    string.Equals(item.Key, "application/x-www-form-urlencoded", StringComparison.OrdinalIgnoreCase) ? 1 :
                    string.Equals(item.Key, "multipart/form-data", StringComparison.OrdinalIgnoreCase) ? 2 :
                    int.MaxValue);

            var kind = ResolveRequestBodyKind(selectedContent.Key);
            return new RequestBodyInfo(
                kind,
                ResolveTypeName(selectedContent.Value.Schema, requestBody.Required),
                requestBody.Required,
                requestBody.Description,
                kind == RequestBodyKind.Json ? [] : GetSupportedRequestBodyProperties(kind, selectedContent.Value.Schema));
        }

        private List<RequestBodyPropertyInfo> GetSupportedRequestBodyProperties(RequestBodyKind requestBodyKind, IOpenApiSchema? schema)
        {
            if (schema is null)
            {
                throw new UnsupportedGenerationException($"{GetRequestBodyContentType(requestBodyKind)} request bodies must declare a schema.");
            }

            if (TryResolveSchemaReferenceName(schema) is not { } schemaName)
            {
                throw new UnsupportedGenerationException($"{GetRequestBodyContentType(requestBodyKind)} request bodies must reference a component schema so code can be generated at compile time.");
            }

            var resolvedSchema = ResolveSchemaReference(schema);
            if (resolvedSchema.OneOf is { Count: > 0 } || resolvedSchema.AnyOf is { Count: > 0 })
            {
                throw new UnsupportedGenerationException($"{GetRequestBodyContentType(requestBodyKind)} request body '{schemaName}' uses oneOf/anyOf, which cannot be generated at compile time.");
            }

            if (TryGetDictionaryValueType(resolvedSchema, out _))
            {
                throw new UnsupportedGenerationException($"{GetRequestBodyContentType(requestBodyKind)} request body '{schemaName}' uses additionalProperties or patternProperties, which are unsupported for compile-time generation.");
            }

            var properties = GetSchemaProperties(resolvedSchema);
            var result = new List<RequestBodyPropertyInfo>(properties.Count);
            foreach (var property in properties)
            {
                result.Add(CreateRequestBodyPropertyInfo(schemaName, requestBodyKind, property));
            }

            return result;
        }

        private RequestBodyPropertyInfo CreateRequestBodyPropertyInfo(string schemaName, RequestBodyKind requestBodyKind, SchemaPropertyInfo property)
        {
            var propertyName = SafeIdentifier(ToPascalCase(property.Name));
            var propertyTypeName = ResolveTypeName(property.Schema, property.Required);
            var valueKind = ClassifyRequestBodyValueKind(schemaName, property.Name, requestBodyKind, property.Schema, out var elementKind);
            var isNullable = propertyTypeName.EndsWith("?", StringComparison.Ordinal);
            var elementNullable = valueKind == RequestBodyValueKind.Collection
                && ResolveTypeName(ResolveSchemaReference(property.Schema).Items, required: true).EndsWith("?", StringComparison.Ordinal);

            return new RequestBodyPropertyInfo(
                property.Name,
                propertyName,
                valueKind,
                isNullable,
                elementKind,
                elementNullable);
        }

        private RequestBodyValueKind ClassifyRequestBodyValueKind(string schemaName, string propertyName, RequestBodyKind requestBodyKind, IOpenApiSchema schema, out RequestBodyValueKind? elementKind)
        {
            var resolvedSchema = ResolveSchemaReference(schema);
            elementKind = null;

            if (resolvedSchema.OneOf is { Count: > 0 } || resolvedSchema.AnyOf is { Count: > 0 })
            {
                throw new UnsupportedGenerationException($"{GetRequestBodyContentType(requestBodyKind)} request body '{schemaName}' property '{propertyName}' uses oneOf/anyOf, which cannot be generated at compile time.");
            }

            if (TryGetDictionaryValueType(resolvedSchema, out _))
            {
                throw new UnsupportedGenerationException($"{GetRequestBodyContentType(requestBodyKind)} request body '{schemaName}' property '{propertyName}' uses additionalProperties or patternProperties, which are unsupported for compile-time generation.");
            }

            if (IsEnumSchema(resolvedSchema))
            {
                return RequestBodyValueKind.Scalar;
            }

            var schemaType = resolvedSchema.Type & ~JsonSchemaType.Null;
            if (schemaType == 0 && resolvedSchema.AllOf is { Count: > 0 })
            {
                var nonNullMembers = resolvedSchema.AllOf.Where(static s => !IsNullOnlySchema(s)).ToList();
                if (nonNullMembers.Count == 1)
                {
                    return ClassifyRequestBodyValueKind(schemaName, propertyName, requestBodyKind, nonNullMembers[0], out elementKind);
                }
            }

            switch (schemaType)
            {
                case JsonSchemaType.Integer:
                case JsonSchemaType.Number:
                case JsonSchemaType.Boolean:
                case JsonSchemaType.String when !string.Equals(resolvedSchema.Format, "binary", StringComparison.OrdinalIgnoreCase):
                    return RequestBodyValueKind.Scalar;
                case JsonSchemaType.String when string.Equals(resolvedSchema.Format, "binary", StringComparison.OrdinalIgnoreCase):
                    if (requestBodyKind == RequestBodyKind.FormUrlEncoded)
                    {
                        throw new UnsupportedGenerationException($"{GetRequestBodyContentType(requestBodyKind)} request body '{schemaName}' property '{propertyName}' uses binary content, which cannot be generated for application/x-www-form-urlencoded.");
                    }

                    return RequestBodyValueKind.Binary;
                case JsonSchemaType.Array:
                    if (resolvedSchema.Items is null)
                    {
                        throw new UnsupportedGenerationException($"{GetRequestBodyContentType(requestBodyKind)} request body '{schemaName}' property '{propertyName}' defines an array without items.");
                    }

                    elementKind = ClassifyCollectionElementKind(schemaName, propertyName, requestBodyKind, resolvedSchema.Items);
                    return RequestBodyValueKind.Collection;
                default:
                    throw new UnsupportedGenerationException($"{GetRequestBodyContentType(requestBodyKind)} request body '{schemaName}' property '{propertyName}' uses an unsupported schema type for compile-time generation.");
            }
        }

        private RequestBodyValueKind ClassifyCollectionElementKind(string schemaName, string propertyName, RequestBodyKind requestBodyKind, IOpenApiSchema schema)
        {
            var resolvedSchema = ResolveSchemaReference(schema);
            if (resolvedSchema.OneOf is { Count: > 0 } || resolvedSchema.AnyOf is { Count: > 0 })
            {
                throw new UnsupportedGenerationException($"{GetRequestBodyContentType(requestBodyKind)} request body '{schemaName}' property '{propertyName}' contains collection elements using oneOf/anyOf, which cannot be generated at compile time.");
            }

            if (TryGetDictionaryValueType(resolvedSchema, out _))
            {
                throw new UnsupportedGenerationException($"{GetRequestBodyContentType(requestBodyKind)} request body '{schemaName}' property '{propertyName}' contains collection elements using additionalProperties or patternProperties, which are unsupported for compile-time generation.");
            }

            if (IsEnumSchema(resolvedSchema))
            {
                return RequestBodyValueKind.Scalar;
            }

            var schemaType = resolvedSchema.Type & ~JsonSchemaType.Null;
            if (schemaType == 0 && resolvedSchema.AllOf is { Count: > 0 })
            {
                var nonNullMembers = resolvedSchema.AllOf.Where(static s => !IsNullOnlySchema(s)).ToList();
                if (nonNullMembers.Count == 1)
                {
                    return ClassifyCollectionElementKind(schemaName, propertyName, requestBodyKind, nonNullMembers[0]);
                }
            }

            return schemaType switch
            {
                JsonSchemaType.Integer or JsonSchemaType.Number or JsonSchemaType.Boolean => RequestBodyValueKind.Scalar,
                JsonSchemaType.String when !string.Equals(resolvedSchema.Format, "binary", StringComparison.OrdinalIgnoreCase) => RequestBodyValueKind.Scalar,
                JsonSchemaType.String when string.Equals(resolvedSchema.Format, "binary", StringComparison.OrdinalIgnoreCase) && requestBodyKind == RequestBodyKind.MultipartFormData => RequestBodyValueKind.Binary,
                _ => throw new UnsupportedGenerationException($"{GetRequestBodyContentType(requestBodyKind)} request body '{schemaName}' property '{propertyName}' contains collection elements with an unsupported schema type for compile-time generation.")
            };
        }

        private static string GetRequestBodyContentType(RequestBodyKind kind)
        {
            return kind switch
            {
                RequestBodyKind.FormUrlEncoded => "application/x-www-form-urlencoded",
                RequestBodyKind.MultipartFormData => "multipart/form-data",
                _ => "application/json"
            };
        }

        private static RequestBodyKind ResolveRequestBodyKind(string contentType)
        {
            if (string.Equals(contentType, "application/x-www-form-urlencoded", StringComparison.OrdinalIgnoreCase))
            {
                return RequestBodyKind.FormUrlEncoded;
            }

            if (string.Equals(contentType, "multipart/form-data", StringComparison.OrdinalIgnoreCase))
            {
                return RequestBodyKind.MultipartFormData;
            }

            return RequestBodyKind.Json;
        }
    }
}
