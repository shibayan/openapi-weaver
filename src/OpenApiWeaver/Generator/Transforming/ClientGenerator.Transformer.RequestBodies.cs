using Microsoft.OpenApi;

namespace OpenApiWeaver;

public sealed partial class ClientGenerator
{
    private sealed partial class Transformer
    {
        private static bool TrySelectRequestBodyContent(IOpenApiRequestBody requestBody, out KeyValuePair<string, IOpenApiMediaType> selectedContent)
            => TrySelectPreferredContent(requestBody.Content, GetRequestBodyContentPriority, out selectedContent);

        private RequestBodyInfo? ResolveRequestBody(IOpenApiRequestBody? requestBody, ISet<string> usedParameterNames)
        {
            if (requestBody is null || !TrySelectRequestBodyContent(requestBody, out var selectedContent))
            {
                return null;
            }

            var schema = selectedContent.Value.Schema;
            if (TryResolveRequestBodyKind(selectedContent.Key, schema) is not { } kind)
            {
                throw new UnsupportedGenerationException(
                    $"Request body content type '{selectedContent.Key}' is not supported. Supported content types are: application/json, application/*+json, application/x-www-form-urlencoded, multipart/form-data, text/*, and binary content such as application/octet-stream.",
                    UnsupportedFeatureKind.RequestBody);
            }

            var (contentType, bodyType) = kind switch
            {
                RequestBodyKind.Binary => (
                    ResolveConcreteRequestMediaType(selectedContent.Key)!,
                    new TypeUsage("byte[]", TypeShape.Binary, schemaAllowsNull: false, isOptional: !requestBody.Required)),
                RequestBodyKind.Text => (
                    RemoveCharsetParameter(ResolveConcreteRequestMediaType(selectedContent.Key)!),
                    new TypeUsage("string", TypeShape.String, schemaAllowsNull: false, isOptional: !requestBody.Required)),
                _ => (selectedContent.Key, _schemaTypeResolver.ResolveTypeUsage(schema, requestBody.Required))
            };

            return new RequestBodyInfo(
                kind,
                contentType,
                bodyType,
                AllocateUniqueName(usedParameterNames, "body", "body"),
                requestBody.Required,
                requestBody.Description,
                kind is RequestBodyKind.FormUrlEncoded or RequestBodyKind.MultipartFormData ? GetSupportedRequestBodyProperties(kind, schema) : []);
        }

        private List<RequestBodyPropertyInfo> GetSupportedRequestBodyProperties(RequestBodyKind requestBodyKind, IOpenApiSchema? schema)
        {
            if (schema is null)
            {
                throw new UnsupportedGenerationException(
                    $"{GetRequestBodyContentType(requestBodyKind)} request bodies must declare a schema.",
                    UnsupportedFeatureKind.RequestBody);
            }

            if (SchemaReferenceResolver.TryResolveSchemaReferenceId(schema) is not { } schemaReferenceId)
            {
                throw new UnsupportedGenerationException(
                    $"{GetRequestBodyContentType(requestBodyKind)} request bodies must reference a named component schema for compile-time code generation.",
                    UnsupportedFeatureKind.RequestBody);
            }

            var resolvedSchema = _schemaReferenceResolver.ResolveSchemaReference(schema);
            var typeName = _schemaReferenceResolver.TryResolveSchemaReferenceName(schema);
            var schemaName = typeName ?? schemaReferenceId;
            if (resolvedSchema.OneOf is { Count: > 0 } || resolvedSchema.AnyOf is { Count: > 0 })
            {
                throw new UnsupportedGenerationException(
                    $"{GetRequestBodyContentType(requestBodyKind)} request body '{schemaName}' uses oneOf/anyOf, which is not supported for compile-time code generation.",
                    UnsupportedFeatureKind.RequestBody);
            }

            if (typeName is null)
            {
                throw new UnsupportedGenerationException(
                    $"{GetRequestBodyContentType(requestBodyKind)} request body '{schemaName}' must reference an object schema for compile-time code generation.",
                    UnsupportedFeatureKind.RequestBody);
            }

            if (_schemaTypeResolver.TryGetDictionaryValueType(resolvedSchema, out _))
            {
                throw new UnsupportedGenerationException(
                    $"{GetRequestBodyContentType(requestBodyKind)} request body '{schemaName}' uses additionalProperties or patternProperties, which is not supported for compile-time code generation.",
                    UnsupportedFeatureKind.RequestBody);
            }

            // Polymorphic models drop the discriminator property and inherit members, so their form fields cannot be mapped yet.
            if (_schemaDefinitionsByTypeName.TryGetValue(typeName, out var schemaDefinition)
                && (schemaDefinition.IsPolymorphicBase || schemaDefinition.BaseTypeName is not null))
            {
                throw new UnsupportedGenerationException(
                    $"{GetRequestBodyContentType(requestBodyKind)} request body '{schemaName}' uses a discriminator-based polymorphic schema, which is not supported for compile-time code generation.",
                    UnsupportedFeatureKind.RequestBody);
            }

            var context = new FormValueContext(
                $"{GetRequestBodyContentType(requestBodyKind)} request body '{schemaName}'",
                allowsBinary: requestBodyKind == RequestBodyKind.MultipartFormData,
                $"{GetRequestBodyContentType(requestBodyKind)} request bodies",
                UnsupportedFeatureKind.RequestBody);
            return CreateFormProperties(context, typeName, resolvedSchema);
        }

        private List<RequestBodyPropertyInfo> CreateFormProperties(FormValueContext context, string typeName, IOpenApiSchema resolvedSchema)
        {
            var propertyNameByJsonName = _schemaDefinitionsByTypeName.TryGetValue(typeName, out var schemaDefinition)
                ? schemaDefinition.Properties.ToDictionary(static p => p.JsonPropertyName, static p => p.PropertyName, StringComparer.Ordinal)
                : new Dictionary<string, string>(StringComparer.Ordinal);

            var properties = SchemaPropertyCollector.Collect(resolvedSchema);
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

                result.Add(CreateFormPropertyInfo(context, property, propertyName));
            }

            return result;
        }

        private RequestBodyPropertyInfo CreateFormPropertyInfo(FormValueContext context, SchemaPropertyInfo property, string propertyName)
        {
            var propertyType = _schemaTypeResolver.ResolveTypeUsage(property.Schema, property.Required);
            var valueKind = ClassifyValueKind(context, property.Name, property.Schema, isArrayItem: false, out var elementKind);
            var isNullable = propertyType.CanBeNullInCSharp;
            var elementNullable = valueKind == RequestBodyValueKind.Collection
                && _schemaTypeResolver.ResolveTypeUsage(_schemaReferenceResolver.ResolveSchemaReference(property.Schema).Items, required: true).CanBeNullInCSharp;

            return new RequestBodyPropertyInfo(
                property.Name,
                propertyName,
                valueKind,
                isNullable,
                elementKind,
                elementNullable);
        }

        private RequestBodyValueKind ClassifyValueKind(FormValueContext context, string propertyName, IOpenApiSchema schema, bool isArrayItem, out RequestBodyValueKind? elementKind)
        {
            var resolvedSchema = _schemaReferenceResolver.ResolveSchemaReference(schema);
            elementKind = null;

            if (resolvedSchema.OneOf is { Count: > 0 } || resolvedSchema.AnyOf is { Count: > 0 })
            {
                throw UnsupportedProperty(context, propertyName, isArrayItem,
                    propertyReason: "uses oneOf/anyOf, which is not supported for compile-time code generation.",
                    arrayItemReason: "has array items using oneOf/anyOf, which is not supported for compile-time code generation.");
            }

            if (_schemaTypeResolver.TryGetDictionaryValueType(resolvedSchema, out _))
            {
                throw UnsupportedProperty(context, propertyName, isArrayItem,
                    propertyReason: "uses additionalProperties or patternProperties, which is not supported for compile-time code generation.",
                    arrayItemReason: "has array items using additionalProperties or patternProperties, which is not supported for compile-time code generation.");
            }

            if (_schemaEnumResolver.IsEnumSchema(resolvedSchema))
            {
                return RequestBodyValueKind.Scalar;
            }

            var schemaType = resolvedSchema.Type & ~JsonSchemaType.Null;
            if (schemaType == 0 && resolvedSchema.AllOf is { Count: > 0 })
            {
                var nonNullMembers = resolvedSchema.AllOf.Where(static s => !SchemaReferenceResolver.IsNullOnlySchema(s)).ToList();
                if (nonNullMembers.Count == 1)
                {
                    return ClassifyValueKind(context, propertyName, nonNullMembers[0], isArrayItem, out elementKind);
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
                        if (!context.AllowsBinary)
                        {
                            throw UnsupportedArrayItemType(context, propertyName);
                        }

                        return RequestBodyValueKind.Binary;
                    }

                    if (!context.AllowsBinary)
                    {
                        throw new UnsupportedGenerationException(
                            $"{context.Subject} property '{propertyName}' uses binary format, which is not supported for {context.BinaryUnsupportedTarget}.",
                            context.FeatureKind);
                    }

                    return RequestBodyValueKind.Binary;
                case JsonSchemaType.Array when !isArrayItem:
                    if (resolvedSchema.Items is null)
                    {
                        throw new UnsupportedGenerationException(
                            $"{context.Subject} property '{propertyName}' defines an array without an items schema.",
                            context.FeatureKind);
                    }

                    elementKind = ClassifyValueKind(context, propertyName, resolvedSchema.Items, isArrayItem: true, out _);
                    return RequestBodyValueKind.Collection;
                default:
                    throw isArrayItem
                        ? UnsupportedArrayItemType(context, propertyName)
                        : new UnsupportedGenerationException(
                            $"{context.Subject} property '{propertyName}' uses a schema type that is not supported for compile-time code generation.",
                            context.FeatureKind);
            }
        }

        private static UnsupportedGenerationException UnsupportedProperty(FormValueContext context, string propertyName, bool isArrayItem, string propertyReason, string arrayItemReason)
            => new(
                $"{context.Subject} property '{propertyName}' {(isArrayItem ? arrayItemReason : propertyReason)}",
                context.FeatureKind);

        private static UnsupportedGenerationException UnsupportedArrayItemType(FormValueContext context, string propertyName)
            => new(
                $"{context.Subject} property '{propertyName}' has array items with a schema type that is not supported for compile-time code generation.",
                context.FeatureKind);

        private static string GetRequestBodyContentType(RequestBodyKind kind)
            => s_requestBodyContentTypes.First(pair => pair.Kind == kind).ContentType;

        private static RequestBodyKind? TryResolveRequestBodyKind(string contentType, IOpenApiSchema? schema)
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

            if (ResolveConcreteRequestMediaType(contentType) is null)
            {
                return null;
            }

            var schemaType = schema?.Type & ~JsonSchemaType.Null;
            var isBinarySchema = schemaType == JsonSchemaType.String
                && string.Equals(schema!.Format, "binary", StringComparison.OrdinalIgnoreCase);
            if (isBinarySchema || string.Equals(mediaType, "application/octet-stream", StringComparison.OrdinalIgnoreCase))
            {
                return RequestBodyKind.Binary;
            }

            if (mediaType.StartsWith("text/", StringComparison.OrdinalIgnoreCase))
            {
                return schema is null || schemaType == JsonSchemaType.String ? RequestBodyKind.Text : null;
            }

            // A media type without a schema (e.g. `image/png: {}`) carries raw bytes.
            return schema is null ? RequestBodyKind.Binary : null;
        }

        // Text bodies are always encoded as UTF-8, so only the declared charset is replaced; other parameters such as
        // `header=absent` on text/csv are preserved.
        private static string RemoveCharsetParameter(string contentType)
            => string.Join("; ", SplitMediaTypeSegments(contentType)
                .Select(static part => part.Trim())
                .Where(static (part, index) => index == 0 || (part.Length > 0 && !IsCharsetParameter(part))));

        private static bool IsCharsetParameter(string parameter)
        {
            var separatorIndex = parameter.IndexOf('=');
            return separatorIndex > 0
                && string.Equals(parameter.Substring(0, separatorIndex).Trim(), "charset", StringComparison.OrdinalIgnoreCase);
        }

        // Splits a media type into its type and parameters on `;`, except inside quoted parameter values
        // such as `note="a;b"`, where `\` escapes the next character (RFC 9110 quoted-string).
        private static List<string> SplitMediaTypeSegments(string contentType)
        {
            var segments = new List<string>();
            var segmentStart = 0;
            var inQuotes = false;
            for (var i = 0; i < contentType.Length; i++)
            {
                switch (contentType[i])
                {
                    case '\\' when inQuotes:
                        i++;
                        break;
                    case '"':
                        inQuotes = !inQuotes;
                        break;
                    case ';' when !inQuotes:
                        segments.Add(contentType.Substring(segmentStart, i - segmentStart));
                        segmentStart = i + 1;
                        break;
                }
            }

            segments.Add(contentType.Substring(segmentStart));
            return segments;
        }

        // A media type range such as `image/*` can only be sent as a concrete type it covers; ranges without
        // a safe concrete choice return null so the request body is reported as unsupported.
        private static string? ResolveConcreteRequestMediaType(string contentType)
        {
            var mediaType = StripMediaTypeParameters(contentType);
            if (mediaType.IndexOf('*') < 0)
            {
                return contentType;
            }

            var concreteMediaType = mediaType.ToLowerInvariant() switch
            {
                "*/*" or "application/*" => "application/octet-stream",
                "text/*" => "text/plain",
                _ => null
            };

            // Only the range itself is replaced; declared parameters such as `version=2` are kept. The type and
            // subtype cannot contain quotes, so the first `;` always starts the parameters.
            var parametersIndex = contentType.IndexOf(';');
            return concreteMediaType is null || parametersIndex < 0
                ? concreteMediaType
                : concreteMediaType + contentType.Substring(parametersIndex);
        }

        private static readonly (RequestBodyKind Kind, string ContentType)[] s_requestBodyContentTypes =
        [
            (RequestBodyKind.Json, "application/json"),
            (RequestBodyKind.FormUrlEncoded, "application/x-www-form-urlencoded"),
            (RequestBodyKind.MultipartFormData, "multipart/form-data"),
        ];

        // Describes where form-style name/value pairs are written (a form request body or an exploded query object)
        // so unsupported property shapes are reported against the right construct.
        private sealed class FormValueContext(string subject, bool allowsBinary, string binaryUnsupportedTarget, UnsupportedFeatureKind featureKind)
        {
            public string Subject { get; } = subject;
            public bool AllowsBinary { get; } = allowsBinary;
            public string BinaryUnsupportedTarget { get; } = binaryUnsupportedTarget;
            public UnsupportedFeatureKind FeatureKind { get; } = featureKind;
        }
    }
}
