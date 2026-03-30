using System.Text;

namespace OpenApiWeaver;

public sealed partial class OpenApiWeaverSourceGenerator
{
    private sealed partial class ClientEmitter
    {
        private void EmitSchemas(StringBuilder builder)
        {
            foreach (var schema in model.Schemas.Where(static schema => schema.ParentTypeName is null))
            {
                EmitSchemaDefinition(builder, schema, "");
                builder.AppendLine();
            }
        }

        private void EmitSchemaDefinition(StringBuilder builder, SchemaDefinition schema, string indent)
        {
            if (schema.IsEnum)
            {
                EmitEnumSchema(builder, schema, indent);
                return;
            }

            EmitSchema(builder, schema, indent);
        }

        private void EmitSchema(StringBuilder builder, SchemaDefinition schema, string indent)
        {
            EmitDocComment(
                builder,
                indent,
                summary: schema.Summary,
                remarks: schema.Description);
            if (schema.DictionaryValueType is not null)
            {
                builder.Append(indent).Append("public sealed class ").Append(schema.DeclaredTypeName).Append(" : Dictionary<string, ").Append(schema.DictionaryValueType).AppendLine(">");
                builder.Append(indent).AppendLine("{");
            }
            else
            {
                builder.Append(indent).Append("public sealed class ").Append(schema.DeclaredTypeName).AppendLine();
                builder.Append(indent).AppendLine("{");
            }

            if (schema.Properties.Count > 0)
            {
                foreach (var property in schema.Properties)
                {
                    var requiredModifier = property.Required ? "required " : string.Empty;
                    EmitDocComment(
                        builder,
                        indent + "    ",
                        summary: property.Summary,
                        remarks: property.Description);
                    if (!property.Required && property.TypeName.EndsWith("?", StringComparison.Ordinal))
                    {
                        builder.Append(indent).AppendLine("    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]");
                    }

                    builder.Append(indent).Append("    [JsonPropertyName(\"").Append(EscapeStringLiteral(property.JsonName)).AppendLine("\")]");
                    builder.Append(indent).Append("    public ").Append(requiredModifier).Append(property.TypeName).Append(' ').Append(property.PropertyName).AppendLine(" { get; init; }");
                }
            }

            var nestedSchemas = model.Schemas.Where(child => string.Equals(child.ParentTypeName, schema.TypeName, StringComparison.Ordinal)).ToList();
            foreach (var nestedSchema in nestedSchemas)
            {
                builder.AppendLine();
                EmitSchemaDefinition(builder, nestedSchema, indent + "    ");
            }

            builder.Append(indent).AppendLine("}");
        }

        private static void EmitEnumSchema(StringBuilder builder, SchemaDefinition schema, string indent)
        {
            if (schema.EnumKind == SchemaEnumKind.Integer)
            {
                EmitIntegerEnumSchema(builder, schema, indent);
                return;
            }

            EmitStringEnumSchema(builder, schema, indent);
        }

        private static void EmitStringEnumSchema(StringBuilder builder, SchemaDefinition schema, string indent)
        {
            EmitDocComment(
                builder,
                indent,
                summary: schema.Summary,
                remarks: schema.Description);
            builder.Append(indent).Append("[JsonConverter(typeof(").Append(schema.DeclaredTypeName).AppendLine("JsonConverter))]");
            builder.Append(indent).Append("public readonly record struct ").Append(schema.DeclaredTypeName).AppendLine("(string Value)");
            builder.Append(indent).AppendLine("{");

            foreach (var enumMember in schema.EnumMembers)
            {
                builder.Append(indent).Append("    public static readonly ").Append(schema.DeclaredTypeName).Append(' ').Append(enumMember.MemberName).Append(" = new(\"").Append(EscapeStringLiteral(enumMember.Value)).AppendLine("\");");
            }

            builder.AppendLine();
            builder.Append(indent).AppendLine("    public override string ToString() => Value;");
            builder.Append(indent).AppendLine("}");
            builder.AppendLine();
            builder.Append(indent).Append("public sealed class ").Append(schema.DeclaredTypeName).Append("JsonConverter : JsonConverter<").Append(schema.DeclaredTypeName).AppendLine(">");
            builder.Append(indent).AppendLine("{");
            builder.Append(indent).Append("    public override ").Append(schema.DeclaredTypeName).AppendLine(" Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)");
            builder.Append(indent).AppendLine("    {");
            builder.Append(indent).Append("        return new ").Append(schema.DeclaredTypeName).AppendLine("(reader.GetString()!);");
            builder.Append(indent).AppendLine("    }");
            builder.AppendLine();
            builder.Append(indent).Append("    public override void Write(Utf8JsonWriter writer, ").Append(schema.DeclaredTypeName).AppendLine(" value, JsonSerializerOptions options)");
            builder.Append(indent).AppendLine("    {");
            builder.Append(indent).AppendLine("        writer.WriteStringValue(value.Value);");
            builder.Append(indent).AppendLine("    }");
            builder.Append(indent).AppendLine("}");
        }

        private static void EmitIntegerEnumSchema(StringBuilder builder, SchemaDefinition schema, string indent)
        {
            EmitDocComment(
                builder,
                indent,
                summary: schema.Summary,
                remarks: schema.Description);
            builder.Append(indent).Append("public enum ").Append(schema.DeclaredTypeName);

            if (!string.IsNullOrWhiteSpace(schema.EnumUnderlyingType) && !string.Equals(schema.EnumUnderlyingType, "int", StringComparison.Ordinal))
            {
                builder.Append(" : ").Append(schema.EnumUnderlyingType);
            }

            builder.AppendLine();
            builder.Append(indent).AppendLine("{");

            for (var i = 0; i < schema.EnumMembers.Count; i++)
            {
                var enumMember = schema.EnumMembers[i];
                builder.Append(indent).Append("    ").Append(enumMember.MemberName).Append(" = ").Append(enumMember.Value);
                builder.AppendLine(i < schema.EnumMembers.Count - 1 ? "," : string.Empty);
            }

            builder.Append(indent).AppendLine("}");
        }
    }
}
