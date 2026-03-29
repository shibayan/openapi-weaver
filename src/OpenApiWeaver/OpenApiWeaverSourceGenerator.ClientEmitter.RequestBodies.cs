using System.Text;

using Microsoft.OpenApi;

namespace OpenApiWeaver;

public sealed partial class OpenApiWeaverSourceGenerator
{
    private sealed partial class ClientEmitter
    {
        private void EmitRequestBodyContentAssignment(StringBuilder builder, RequestBodyInfo requestBody, bool nullableBody)
        {
            var bodyValue = nullableBody ? "body!" : "body";

            switch (requestBody.Kind)
            {
                case RequestBodyKind.FormUrlEncoded:
                    EmitFormUrlEncodedContentAssignment(builder, requestBody, bodyValue);
                    break;
                case RequestBodyKind.MultipartFormData:
                    EmitMultipartFormDataContentAssignment(builder, requestBody, bodyValue);
                    break;
                default:
                    builder.Append("            request.Content = JsonContent.Create(").Append(bodyValue).AppendLine(", options: OpenApiClientHelpers.SerializerOptions);");
                    break;
            }
        }

        private void EmitFormUrlEncodedContentAssignment(StringBuilder builder, RequestBodyInfo requestBody, string bodyValue)
        {
            var properties = GetSupportedRequestBodyProperties(requestBody);

            builder.AppendLine("            var values = new List<KeyValuePair<string, string>>();");
            foreach (var property in properties)
            {
                var propertyAccess = $"{bodyValue}.{property.PropertyName}";
                EmitPropertyNullCheckStart(builder, propertyAccess, property.Nullable, indentLevel: 3);

                switch (property.Kind)
                {
                    case RequestBodyValueKind.Scalar:
                        EmitFormValueAdd(builder, property.SerializedName, propertyAccess, indentLevel: property.Nullable ? 4 : 3);
                        break;
                    case RequestBodyValueKind.Collection:
                        EmitCollectionLoopStart(builder, propertyAccess, indentLevel: property.Nullable ? 4 : 3);
                        EmitElementNullCheckStart(builder, property.ElementNullable, indentLevel: property.Nullable ? 5 : 4);
                        EmitFormValueAdd(builder, property.SerializedName, "item", indentLevel: property.Nullable ? (property.ElementNullable ? 6 : 5) : (property.ElementNullable ? 5 : 4));
                        EmitElementNullCheckEnd(builder, property.ElementNullable, indentLevel: property.Nullable ? 5 : 4);
                        EmitCollectionLoopEnd(builder, indentLevel: property.Nullable ? 4 : 3);
                        break;
                }

                EmitPropertyNullCheckEnd(builder, property.Nullable, indentLevel: 3);
            }

            builder.AppendLine("            request.Content = new FormUrlEncodedContent(values);");
        }

        private void EmitMultipartFormDataContentAssignment(StringBuilder builder, RequestBodyInfo requestBody, string bodyValue)
        {
            var properties = GetSupportedRequestBodyProperties(requestBody);

            builder.AppendLine("            var content = new MultipartFormDataContent();");
            foreach (var property in properties)
            {
                var propertyAccess = $"{bodyValue}.{property.PropertyName}";
                EmitPropertyNullCheckStart(builder, propertyAccess, property.Nullable, indentLevel: 3);

                switch (property.Kind)
                {
                    case RequestBodyValueKind.Scalar:
                    case RequestBodyValueKind.Binary:
                        EmitMultipartValueAdd(builder, property.SerializedName, propertyAccess, property.Kind, indentLevel: property.Nullable ? 4 : 3);
                        break;
                    case RequestBodyValueKind.Collection:
                        EmitCollectionLoopStart(builder, propertyAccess, indentLevel: property.Nullable ? 4 : 3);
                        EmitElementNullCheckStart(builder, property.ElementNullable, indentLevel: property.Nullable ? 5 : 4);
                        EmitMultipartValueAdd(builder, property.SerializedName, "item", property.ElementKind!.Value, indentLevel: property.Nullable ? (property.ElementNullable ? 6 : 5) : (property.ElementNullable ? 5 : 4));
                        EmitElementNullCheckEnd(builder, property.ElementNullable, indentLevel: property.Nullable ? 5 : 4);
                        EmitCollectionLoopEnd(builder, indentLevel: property.Nullable ? 4 : 3);
                        break;
                }

                EmitPropertyNullCheckEnd(builder, property.Nullable, indentLevel: 3);
            }

            builder.AppendLine("            request.Content = content;");
        }

        private List<RequestBodyPropertyInfo> GetSupportedRequestBodyProperties(RequestBodyInfo requestBody)
        {
            if (requestBody.Schema is null)
            {
                throw new UnsupportedGenerationException($"{GetRequestBodyContentType(requestBody.Kind)} request bodies must declare a schema.");
            }

            if (TryResolveSchemaReferenceName(requestBody.Schema) is not { } schemaName)
            {
                throw new UnsupportedGenerationException($"{GetRequestBodyContentType(requestBody.Kind)} request bodies must reference a component schema so code can be generated at compile time.");
            }

            var resolvedSchema = ResolveSchemaReference(requestBody.Schema);
            if (resolvedSchema.OneOf is { Count: > 0 } || resolvedSchema.AnyOf is { Count: > 0 })
            {
                throw new UnsupportedGenerationException($"{GetRequestBodyContentType(requestBody.Kind)} request body '{schemaName}' uses oneOf/anyOf, which cannot be generated at compile time.");
            }

            if (TryGetDictionaryValueType(resolvedSchema, out _))
            {
                throw new UnsupportedGenerationException($"{GetRequestBodyContentType(requestBody.Kind)} request body '{schemaName}' uses additionalProperties or patternProperties, which are unsupported for compile-time generation.");
            }

            var properties = GetSchemaProperties(resolvedSchema);
            var result = new List<RequestBodyPropertyInfo>(properties.Count);
            foreach (var property in properties)
            {
                result.Add(CreateRequestBodyPropertyInfo(schemaName, requestBody.Kind, property));
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
                var nonNullMembers = resolvedSchema.AllOf.Where(s => !IsNullOnlySchema(s)).ToList();
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
                var nonNullMembers = resolvedSchema.AllOf.Where(s => !IsNullOnlySchema(s)).ToList();
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

        private IOpenApiSchema ResolveSchemaReference(IOpenApiSchema schema)
        {
            if (schema is IOpenApiReferenceHolder<JsonSchemaReference> { Reference.Id: not null } referenceHolder
                && _document.Components?.Schemas is { } schemas
                && schemas.TryGetValue(referenceHolder.Reference.Id, out var resolvedSchema))
            {
                return resolvedSchema;
            }

            return schema;
        }

        private static void EmitFormValueAdd(StringBuilder builder, string name, string valueExpression, int indentLevel)
        {
            builder.Append(' ', indentLevel * 4)
                .Append("values.Add(new KeyValuePair<string, string>(\"")
                .Append(EscapeStringLiteral(name))
                .Append("\", OpenApiClientHelpers.FormatParameter(")
                .Append(valueExpression)
                .AppendLine(")));");
        }

        private static void EmitMultipartValueAdd(StringBuilder builder, string name, string valueExpression, RequestBodyValueKind kind, int indentLevel)
        {
            builder.Append(' ', indentLevel * 4).Append("content.Add(");
            switch (kind)
            {
                case RequestBodyValueKind.Binary:
                    builder.Append("new ByteArrayContent(").Append(valueExpression).Append(")");
                    builder.Append(", \"").Append(EscapeStringLiteral(name)).Append("\", \"").Append(EscapeStringLiteral(name)).AppendLine("\");");
                    break;
                case RequestBodyValueKind.Scalar:
                    builder.Append("new StringContent(OpenApiClientHelpers.FormatParameter(").Append(valueExpression).Append("))")
                        .Append(", \"").Append(EscapeStringLiteral(name)).AppendLine("\");");
                    break;
                default:
                    throw new InvalidOperationException("Unsupported multipart value kind.");
            }
        }

        private static void EmitPropertyNullCheckStart(StringBuilder builder, string propertyAccess, bool nullable, int indentLevel)
        {
            if (!nullable)
            {
                return;
            }

            builder.Append(' ', indentLevel * 4).Append("if (").Append(propertyAccess).AppendLine(" is not null)");
            builder.Append(' ', indentLevel * 4).AppendLine("{");
        }

        private static void EmitPropertyNullCheckEnd(StringBuilder builder, bool nullable, int indentLevel)
        {
            if (!nullable)
            {
                return;
            }

            builder.Append(' ', indentLevel * 4).AppendLine("}");
        }

        private static void EmitCollectionLoopStart(StringBuilder builder, string propertyAccess, int indentLevel)
        {
            builder.Append(' ', indentLevel * 4).Append("foreach (var item in ").Append(propertyAccess).AppendLine(")");
            builder.Append(' ', indentLevel * 4).AppendLine("{");
        }

        private static void EmitCollectionLoopEnd(StringBuilder builder, int indentLevel)
        {
            builder.Append(' ', indentLevel * 4).AppendLine("}");
        }

        private static void EmitElementNullCheckStart(StringBuilder builder, bool nullable, int indentLevel)
        {
            if (!nullable)
            {
                return;
            }

            builder.Append(' ', indentLevel * 4).AppendLine("if (item is not null)");
            builder.Append(' ', indentLevel * 4).AppendLine("{");
        }

        private static void EmitElementNullCheckEnd(StringBuilder builder, bool nullable, int indentLevel)
        {
            if (!nullable)
            {
                return;
            }

            builder.Append(' ', indentLevel * 4).AppendLine("}");
        }
    }
}
