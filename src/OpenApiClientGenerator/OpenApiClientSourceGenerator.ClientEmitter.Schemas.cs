using System.Globalization;
using System.Text;

using Microsoft.OpenApi;

namespace OpenApiClientGenerator;

public sealed partial class OpenApiClientSourceGenerator
{
    private sealed partial class ClientEmitter
    {
        private void EmitSchemas(StringBuilder builder)
        {
            if (_document.Components?.Schemas is null)
            {
                return;
            }

            foreach (var schema in _document.Components.Schemas)
            {
                if (IsEnumSchema(schema.Value))
                {
                    EmitEnumSchema(builder, _schemaNames[schema.Key], schema.Value);
                }
                else
                {
                    EmitSchema(builder, _schemaNames[schema.Key], schema.Value);
                }

                builder.AppendLine();
            }
        }

        private void EmitSchema(StringBuilder builder, string typeName, IOpenApiSchema schema)
        {
            var properties = GetSchemaProperties(schema);
            var hasDictionaryShape = TryGetDictionaryValueType(schema, out var dictionaryValueType);

            EmitDocComment(
                builder,
                "    ",
                summary: schema.Title ?? typeName,
                remarks: schema.Description);
            if (hasDictionaryShape)
            {
                builder.Append("public sealed class ").Append(typeName).Append(" : Dictionary<string, ").Append(dictionaryValueType).AppendLine(">");
                builder.AppendLine("{");
            }
            else
            {
                builder.Append("public sealed class ").Append(typeName).AppendLine();
                builder.AppendLine("{");
            }

            if (properties.Count > 0)
            {
                foreach (var property in properties)
                {
                    var propertyName = SafeIdentifier(ToPascalCase(property.Name));
                    var propertyType = ResolveTypeName(property.Schema, property.Required);
                    var requiredModifier = property.Required ? "required " : string.Empty;
                    EmitDocComment(
                        builder,
                        "        ",
                        summary: property.Schema.Title ?? propertyName,
                        remarks: property.Schema.Description);
                    builder.Append("    [JsonPropertyName(\"").Append(EscapeStringLiteral(property.Name)).AppendLine("\")]");
                    builder.Append("    public ").Append(requiredModifier).Append(propertyType).Append(' ').Append(propertyName).AppendLine(" { get; init; }");
                }
            }

            builder.AppendLine("}");
        }

        private static bool IsEnumSchema(IOpenApiSchema schema)
        {
            return schema.Enum is { Count: > 0 } && HasSchemaType(schema, JsonSchemaType.String);
        }

        private static void EmitEnumSchema(StringBuilder builder, string typeName, IOpenApiSchema schema)
        {
            EmitDocComment(
                builder,
                "    ",
                summary: schema.Title ?? typeName,
                remarks: schema.Description);
            builder.Append("[JsonConverter(typeof(").Append(typeName).AppendLine("JsonConverter))]");
            builder.Append("public readonly record struct ").Append(typeName).AppendLine("(string Value)");
            builder.AppendLine("{");

            foreach (var item in schema.Enum ?? [])
            {
                var enumValue = item?.ToString();
                if (string.IsNullOrWhiteSpace(enumValue))
                {
                    continue;
                }

                var memberName = SafeIdentifier(ToPascalCase(enumValue ?? string.Empty));
                builder.Append("    public static readonly ").Append(typeName).Append(' ').Append(memberName).Append(" = new(\"").Append(EscapeStringLiteral(enumValue ?? string.Empty)).AppendLine("\");");
            }

            builder.AppendLine();
            builder.AppendLine("    public override string ToString() => Value;");
            builder.AppendLine("}");
            builder.AppendLine();
            builder.Append("public sealed class ").Append(typeName).Append("JsonConverter : JsonConverter<").Append(typeName).AppendLine(">");
            builder.AppendLine("{");
            builder.Append("    public override ").Append(typeName).AppendLine(" Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)");
            builder.AppendLine("    {");
            builder.Append("        return new ").Append(typeName).AppendLine("(reader.GetString()!);");
            builder.AppendLine("    }");
            builder.AppendLine();
            builder.Append("    public override void Write(Utf8JsonWriter writer, ").Append(typeName).AppendLine(" value, JsonSerializerOptions options)");
            builder.AppendLine("    {");
            builder.AppendLine("        writer.WriteStringValue(value.Value);");
            builder.AppendLine("    }");
            builder.AppendLine("}");
        }

        private string ResolveTypeName(IOpenApiSchema? schema, bool required)
        {
            if (schema is null)
            {
                return required ? "string" : "string?";
            }

            if (schema is IOpenApiReferenceHolder<JsonSchemaReference> { Reference.Id: not null } referenceHolder
                && _schemaNames.TryGetValue(referenceHolder.Reference.Id, out var schemaName))
            {
                return required && !IsNullableSchema(schema) ? schemaName : $"{schemaName}?";
            }

            if (TryResolveCompositeTypeName(schema, required, out var compositeTypeName))
            {
                return compositeTypeName;
            }

            if (TryGetDictionaryValueType(schema, out var dictionaryValueType))
            {
                var dictionaryType = $"IReadOnlyDictionary<string, {dictionaryValueType}>";
                return required && !IsNullableSchema(schema) ? dictionaryType : $"{dictionaryType}?";
            }

            var typeName = schema.Type switch
            {
                JsonSchemaType.Integer when string.Equals(schema.Format, "int64", StringComparison.OrdinalIgnoreCase) => "long",
                JsonSchemaType.Integer => "int",
                JsonSchemaType.Number when string.Equals(schema.Format, "float", StringComparison.OrdinalIgnoreCase) => "float",
                JsonSchemaType.Number => "double",
                JsonSchemaType.Boolean => "bool",
                JsonSchemaType.String when string.Equals(schema.Format, "date", StringComparison.OrdinalIgnoreCase) => "DateOnly",
                JsonSchemaType.String when string.Equals(schema.Format, "date-time", StringComparison.OrdinalIgnoreCase) => "DateTimeOffset",
                JsonSchemaType.String when string.Equals(schema.Format, "uuid", StringComparison.OrdinalIgnoreCase) => "Guid",
                JsonSchemaType.String when string.Equals(schema.Format, "binary", StringComparison.OrdinalIgnoreCase) => "byte[]",
                JsonSchemaType.Array => $"IReadOnlyList<{ResolveTypeName(schema.Items, required: true)}>",
                JsonSchemaType.String => "string",
                _ => "JsonElement"
            };

            return required && !IsNullableSchema(schema) ? typeName : $"{typeName}?";
        }

        private List<SchemaPropertyInfo> GetSchemaProperties(IOpenApiSchema schema)
        {
            var properties = new List<SchemaPropertyInfo>();
            var indices = new Dictionary<string, int>(StringComparer.Ordinal);
            CollectSchemaProperties(schema, properties, indices, new HashSet<string>(StringComparer.Ordinal));
            return properties;
        }

        private void CollectSchemaProperties(
            IOpenApiSchema schema,
            List<SchemaPropertyInfo> properties,
            Dictionary<string, int> indices,
            HashSet<string> visited)
        {
            var identity = GetSchemaIdentity(schema);
            if (!visited.Add(identity))
            {
                return;
            }

            if (schema.AllOf is not null)
            {
                foreach (var child in schema.AllOf)
                {
                    CollectSchemaProperties(child, properties, indices, visited);
                }
            }

            if (schema.Properties is not null)
            {
                foreach (var property in schema.Properties)
                {
                    var item = new SchemaPropertyInfo(
                        property.Key,
                        property.Value,
                        schema.Required?.Contains(property.Key) == true);

                    if (indices.TryGetValue(property.Key, out var index))
                    {
                        properties[index] = item;
                    }
                    else
                    {
                        indices.Add(property.Key, properties.Count);
                        properties.Add(item);
                    }
                }
            }

            visited.Remove(identity);
        }

        private static string GetSchemaIdentity(IOpenApiSchema schema)
        {
            if (schema is IOpenApiReferenceHolder<JsonSchemaReference> { Reference.Id: not null } referenceHolder)
            {
                return $"ref:{referenceHolder.Reference.Id}";
            }

            return $"obj:{System.Runtime.CompilerServices.RuntimeHelpers.GetHashCode(schema).ToString(CultureInfo.InvariantCulture)}";
        }

        private string? TryResolveSchemaReferenceName(IOpenApiSchema? schema)
        {
            if (schema is IOpenApiReferenceHolder<JsonSchemaReference> { Reference.Id: not null } referenceHolder
                && _schemaNames.TryGetValue(referenceHolder.Reference.Id, out var schemaName))
            {
                return schemaName;
            }

            return null;
        }

        private bool TryResolveCompositeTypeName(IOpenApiSchema schema, bool required, out string typeName)
        {
            if (TryResolveSchemaUnion(schema.OneOf, required, out typeName))
            {
                return true;
            }

            if (TryResolveSchemaUnion(schema.AnyOf, required, out typeName))
            {
                return true;
            }

            if (TryResolveSchemaUnion(schema.AllOf, required, out typeName))
            {
                return true;
            }

            typeName = string.Empty;
            return false;
        }

        private bool TryResolveSchemaUnion(IList<IOpenApiSchema>? schemas, bool required, out string typeName)
        {
            typeName = string.Empty;
            if (schemas is null || schemas.Count == 0)
            {
                return false;
            }

            var nullable = false;
            var memberTypeNames = new HashSet<string>(StringComparer.Ordinal);
            foreach (var child in schemas)
            {
                if (IsNullOnlySchema(child))
                {
                    nullable = true;
                    continue;
                }

                var childTypeName = TryResolveSchemaReferenceName(child) ?? ResolveTypeName(child, required: true);
                memberTypeNames.Add(TrimNullableTypeName(childTypeName));
            }

            if (memberTypeNames.Count != 1)
            {
                return false;
            }

            var resolvedTypeName = memberTypeNames.Single();
            typeName = nullable || !required ? MakeNullableTypeName(resolvedTypeName) : resolvedTypeName;
            return true;
        }

        private bool TryGetDictionaryValueType(IOpenApiSchema schema, out string valueType)
        {
            valueType = string.Empty;
            var dictionaryValueTypes = new HashSet<string>(StringComparer.Ordinal);
            CollectDictionaryValueTypes(schema, dictionaryValueTypes, new HashSet<string>(StringComparer.Ordinal));
            if (dictionaryValueTypes.Count == 0)
            {
                return false;
            }

            if (dictionaryValueTypes.Count > 1)
            {
                valueType = "JsonElement";
                return true;
            }

            valueType = dictionaryValueTypes.Single();
            return true;
        }

        private void CollectDictionaryValueTypes(
            IOpenApiSchema schema,
            HashSet<string> valueTypes,
            HashSet<string> visited)
        {
            var identity = GetSchemaIdentity(schema);
            if (!visited.Add(identity))
            {
                return;
            }

            if (schema.AdditionalProperties is not null)
            {
                valueTypes.Add(TrimNullableTypeName(ResolveTypeName(schema.AdditionalProperties, required: true)));
            }

            if (schema.PatternProperties is not null)
            {
                foreach (var patternProperty in schema.PatternProperties)
                {
                    valueTypes.Add(TrimNullableTypeName(ResolveTypeName(patternProperty.Value, required: true)));
                }
            }

            if (schema.AllOf is not null)
            {
                foreach (var child in schema.AllOf)
                {
                    CollectDictionaryValueTypes(child, valueTypes, visited);
                }
            }

            visited.Remove(identity);
        }
    }
}
