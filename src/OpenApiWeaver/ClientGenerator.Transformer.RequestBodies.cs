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

            if (TryResolveRequestBodyKind(selectedContent.Key) is not { } kind)
            {
                throw new UnsupportedGenerationException($"Request body content type '{selectedContent.Key}' is not supported. Supported content types are: application/json, application/*+json, application/x-www-form-urlencoded, multipart/form-data.");
            }

            return new RequestBodyInfo(
                kind,
                selectedContent.Key,
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

            var propertyNameByJsonName = _schemaDefinitionsByTypeName.TryGetValue(schemaName, out var schemaDefinition)
                ? schemaDefinition.Properties.ToDictionary(static p => p.JsonPropertyName, static p => p.PropertyName, StringComparer.Ordinal)
                : new Dictionary<string, string>(StringComparer.Ordinal);

            var properties = GetSchemaProperties(resolvedSchema);
            var result = new List<RequestBodyPropertyInfo>(properties.Count);
            foreach (var property in properties)
            {
                if (property.ReadOnly)
                {
                    continue;
                }

                if (!propertyNameByJsonName.TryGetValue(property.Name, out var propertyName))
                {
                    propertyName = NormalizePascalIdentifier(property.Name, "Value");
                }

                result.Add(CreateRequestBodyPropertyInfo(schemaName, requestBodyKind, property, propertyName));
            }

            return result;
        }

        private RequestBodyPropertyInfo CreateRequestBodyPropertyInfo(string schemaName, RequestBodyKind requestBodyKind, SchemaPropertyInfo property, string propertyName)
        {
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
            => ClassifyValueKind(schemaName, propertyName, requestBodyKind, schema, isArrayItem: false, out elementKind);

        private RequestBodyValueKind ClassifyCollectionElementKind(string schemaName, string propertyName, RequestBodyKind requestBodyKind, IOpenApiSchema schema)
            => ClassifyValueKind(schemaName, propertyName, requestBodyKind, schema, isArrayItem: true, out _);

        private RequestBodyValueKind ClassifyValueKind(string schemaName, string propertyName, RequestBodyKind requestBodyKind, IOpenApiSchema schema, bool isArrayItem, out RequestBodyValueKind? elementKind)
        {
            var resolvedSchema = ResolveSchemaReference(schema);
            elementKind = null;

            if (resolvedSchema.OneOf is { Count: > 0 } || resolvedSchema.AnyOf is { Count: > 0 })
            {
                throw UnsupportedProperty(requestBodyKind, schemaName, propertyName, isArrayItem,
                    propertyReason: "uses oneOf/anyOf, which is not supported for compile-time code generation.",
                    arrayItemReason: "has array items using oneOf/anyOf, which is not supported for compile-time code generation.");
            }

            if (TryGetDictionaryValueType(resolvedSchema, out _))
            {
                throw UnsupportedProperty(requestBodyKind, schemaName, propertyName, isArrayItem,
                    propertyReason: "uses additionalProperties or patternProperties, which is not supported for compile-time code generation.",
                    arrayItemReason: "has array items using additionalProperties or patternProperties, which is not supported for compile-time code generation.");
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
                    return ClassifyValueKind(schemaName, propertyName, requestBodyKind, nonNullMembers[0], isArrayItem, out elementKind);
                }
            }

            var isBinaryString = schemaType == JsonSchemaType.String
                && string.Equals(resolvedSchema.Format, "binary", StringComparison.OrdinalIgnoreCase);

            switch (schemaType)
            {
                case JsonSchemaType.Integer:
                case JsonSchemaType.Number:
                case JsonSchemaType.Boolean:
                    return RequestBodyValueKind.Scalar;
                case JsonSchemaType.String when !isBinaryString:
                    return RequestBodyValueKind.Scalar;
                case JsonSchemaType.String when isBinaryString:
                    if (isArrayItem)
                    {
                        if (requestBodyKind != RequestBodyKind.MultipartFormData)
                        {
                            throw UnsupportedArrayItemType(requestBodyKind, schemaName, propertyName);
                        }

                        return RequestBodyValueKind.Binary;
                    }

                    if (requestBodyKind == RequestBodyKind.FormUrlEncoded)
                    {
                        throw new UnsupportedGenerationException($"{GetRequestBodyContentType(requestBodyKind)} request body '{schemaName}' property '{propertyName}' uses binary format, which is not supported for application/x-www-form-urlencoded request bodies.");
                    }

                    return RequestBodyValueKind.Binary;
                case JsonSchemaType.Array when !isArrayItem:
                    if (resolvedSchema.Items is null)
                    {
                        throw new UnsupportedGenerationException($"{GetRequestBodyContentType(requestBodyKind)} request body '{schemaName}' property '{propertyName}' defines an array without an items schema.");
                    }

                    elementKind = ClassifyCollectionElementKind(schemaName, propertyName, requestBodyKind, resolvedSchema.Items);
                    return RequestBodyValueKind.Collection;
                default:
                    throw isArrayItem
                        ? UnsupportedArrayItemType(requestBodyKind, schemaName, propertyName)
                        : new UnsupportedGenerationException($"{GetRequestBodyContentType(requestBodyKind)} request body '{schemaName}' property '{propertyName}' uses a schema type that is not supported for compile-time code generation.");
            }
        }

        private static UnsupportedGenerationException UnsupportedProperty(RequestBodyKind kind, string schemaName, string propertyName, bool isArrayItem, string propertyReason, string arrayItemReason)
            => new($"{GetRequestBodyContentType(kind)} request body '{schemaName}' property '{propertyName}' {(isArrayItem ? arrayItemReason : propertyReason)}");

        private static UnsupportedGenerationException UnsupportedArrayItemType(RequestBodyKind kind, string schemaName, string propertyName)
            => new($"{GetRequestBodyContentType(kind)} request body '{schemaName}' property '{propertyName}' has array items with a schema type that is not supported for compile-time code generation.");

        private static string GetRequestBodyContentType(RequestBodyKind kind)
            => s_requestBodyContentTypes.First(pair => pair.Kind == kind).ContentType;

        private static RequestBodyKind? TryResolveRequestBodyKind(string contentType)
        {
            var mediaType = StripMediaTypeParameters(contentType);

            if (IsJsonMediaType(mediaType))
            {
                return RequestBodyKind.Json;
            }

            foreach (var (mapped, knownType) in s_requestBodyContentTypes)
            {
                if (mapped == RequestBodyKind.Json)
                {
                    continue;
                }

                if (string.Equals(mediaType, knownType, StringComparison.OrdinalIgnoreCase))
                {
                    return mapped;
                }
            }

            return null;
        }

        private static bool IsJsonMediaType(string mediaType)
        {
            if (string.Equals(mediaType, "application/json", StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }

            return mediaType.StartsWith("application/", StringComparison.OrdinalIgnoreCase)
                && mediaType.EndsWith("+json", StringComparison.OrdinalIgnoreCase);
        }

        private static string StripMediaTypeParameters(string contentType)
        {
            var separatorIndex = contentType.IndexOf(';');
            return separatorIndex < 0 ? contentType.Trim() : contentType.Substring(0, separatorIndex).Trim();
        }

        private static readonly (RequestBodyKind Kind, string ContentType)[] s_requestBodyContentTypes =
        [
            (RequestBodyKind.Json, "application/json"),
            (RequestBodyKind.FormUrlEncoded, "application/x-www-form-urlencoded"),
            (RequestBodyKind.MultipartFormData, "multipart/form-data"),
        ];
    }
}
