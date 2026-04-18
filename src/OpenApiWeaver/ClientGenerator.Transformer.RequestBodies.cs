using Microsoft.OpenApi;

namespace OpenApiWeaver;

public sealed partial class ClientGenerator
{
    private sealed partial class Transformer
    {
        private RequestBodyInfo? ResolveRequestBody(IOpenApiRequestBody? requestBody, ISet<string> usedParameterNames)
        {
            if (requestBody is null || !TrySelectPreferredContent(requestBody.Content, GetRequestBodyContentPriority, out var selectedContent))
            {
                return null;
            }

            var kind = ResolveRequestBodyKind(selectedContent.Key);
            return new RequestBodyInfo(
                kind,
                ResolveTypeUsage(selectedContent.Value.Schema, requestBody.Required),
                AllocateUniqueName(usedParameterNames, "body", "body"),
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
                throw new UnsupportedGenerationException($"{GetRequestBodyContentType(requestBodyKind)} request bodies must reference a named component schema for compile-time code generation.");
            }

            var resolvedSchema = ResolveSchemaReference(schema);
            if (resolvedSchema.OneOf is { Count: > 0 } || resolvedSchema.AnyOf is { Count: > 0 })
            {
                throw new UnsupportedGenerationException($"{GetRequestBodyContentType(requestBodyKind)} request body '{schemaName}' uses oneOf/anyOf, which is not supported for compile-time code generation.");
            }

            if (TryGetDictionaryValueType(resolvedSchema, out _))
            {
                throw new UnsupportedGenerationException($"{GetRequestBodyContentType(requestBodyKind)} request body '{schemaName}' uses additionalProperties or patternProperties, which is not supported for compile-time code generation.");
            }

            var properties = GetSchemaProperties(resolvedSchema);
            var result = new List<RequestBodyPropertyInfo>(properties.Count);
            var usedPropertyNames = new HashSet<string>(StringComparer.Ordinal);
            foreach (var property in properties)
            {
                result.Add(CreateRequestBodyPropertyInfo(schemaName, requestBodyKind, property, usedPropertyNames));
            }

            return result;
        }

        private RequestBodyPropertyInfo CreateRequestBodyPropertyInfo(string schemaName, RequestBodyKind requestBodyKind, SchemaPropertyInfo property, ISet<string> usedPropertyNames)
        {
            var propertyName = AllocateUniqueName(
                usedPropertyNames,
                NormalizePascalIdentifier(property.Name, "Value"),
                "Value");
            var propertyType = ResolveTypeUsage(property.Schema, property.Required);
            var valueKind = ClassifyRequestBodyValueKind(schemaName, property.Name, requestBodyKind, property.Schema, out var elementKind);
            var isNullable = propertyType.CanBeNullInCSharp;
            var elementNullable = valueKind == RequestBodyValueKind.Collection
                && ResolveTypeUsage(ResolveSchemaReference(property.Schema).Items, required: true).CanBeNullInCSharp;

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
                throw new UnsupportedGenerationException($"{GetRequestBodyContentType(requestBodyKind)} request body '{schemaName}' property '{propertyName}' uses oneOf/anyOf, which is not supported for compile-time code generation.");
            }

            if (TryGetDictionaryValueType(resolvedSchema, out _))
            {
                throw new UnsupportedGenerationException($"{GetRequestBodyContentType(requestBodyKind)} request body '{schemaName}' property '{propertyName}' uses additionalProperties or patternProperties, which is not supported for compile-time code generation.");
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
                        throw new UnsupportedGenerationException($"{GetRequestBodyContentType(requestBodyKind)} request body '{schemaName}' property '{propertyName}' uses binary format, which is not supported for application/x-www-form-urlencoded request bodies.");
                    }

                    return RequestBodyValueKind.Binary;
                case JsonSchemaType.Array:
                    if (resolvedSchema.Items is null)
                    {
                        throw new UnsupportedGenerationException($"{GetRequestBodyContentType(requestBodyKind)} request body '{schemaName}' property '{propertyName}' defines an array without an items schema.");
                    }

                    elementKind = ClassifyCollectionElementKind(schemaName, propertyName, requestBodyKind, resolvedSchema.Items);
                    return RequestBodyValueKind.Collection;
                default:
                    throw new UnsupportedGenerationException($"{GetRequestBodyContentType(requestBodyKind)} request body '{schemaName}' property '{propertyName}' uses a schema type that is not supported for compile-time code generation.");
            }
        }

        private RequestBodyValueKind ClassifyCollectionElementKind(string schemaName, string propertyName, RequestBodyKind requestBodyKind, IOpenApiSchema schema)
        {
            var resolvedSchema = ResolveSchemaReference(schema);
            if (resolvedSchema.OneOf is { Count: > 0 } || resolvedSchema.AnyOf is { Count: > 0 })
            {
                throw new UnsupportedGenerationException($"{GetRequestBodyContentType(requestBodyKind)} request body '{schemaName}' property '{propertyName}' has array items using oneOf/anyOf, which is not supported for compile-time code generation.");
            }

            if (TryGetDictionaryValueType(resolvedSchema, out _))
            {
                throw new UnsupportedGenerationException($"{GetRequestBodyContentType(requestBodyKind)} request body '{schemaName}' property '{propertyName}' has array items using additionalProperties or patternProperties, which is not supported for compile-time code generation.");
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
                _ => throw new UnsupportedGenerationException($"{GetRequestBodyContentType(requestBodyKind)} request body '{schemaName}' property '{propertyName}' has array items with a schema type that is not supported for compile-time code generation.")
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
