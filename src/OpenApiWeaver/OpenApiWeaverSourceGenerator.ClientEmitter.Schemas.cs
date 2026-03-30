using System.Text;

namespace OpenApiWeaver;

public sealed partial class OpenApiWeaverSourceGenerator
{
    private sealed partial class ClientEmitter
    {
        private void EmitSchemas(StringBuilder builder)
        {
            foreach (var schema in model.Schemas)
            {
                if (schema.IsEnum)
                {
                    EmitEnumSchema(builder, schema);
                }
                else
                {
                    EmitSchema(builder, schema);
                }

                builder.AppendLine();
            }
        }

        private void EmitSchema(StringBuilder builder, SchemaDefinition schema)
        {
            EmitDocComment(
                builder,
                "    ",
                summary: schema.Summary,
                remarks: schema.Description);
            if (schema.DictionaryValueType is not null)
            {
                builder.Append("public sealed class ").Append(schema.TypeName).Append(" : Dictionary<string, ").Append(schema.DictionaryValueType).AppendLine(">");
                builder.AppendLine("{");
            }
            else
            {
                builder.Append("public sealed class ").Append(schema.TypeName).AppendLine();
                builder.AppendLine("{");
            }

            if (schema.Properties.Count > 0)
            {
                foreach (var property in schema.Properties)
                {
                    var requiredModifier = property.Required ? "required " : string.Empty;
                    EmitDocComment(
                        builder,
                        "        ",
                        summary: property.Summary,
                        remarks: property.Description);
                    builder.Append("    [JsonPropertyName(\"").Append(EscapeStringLiteral(property.JsonName)).AppendLine("\")]");
                    builder.Append("    public ").Append(requiredModifier).Append(property.TypeName).Append(' ').Append(property.PropertyName).AppendLine(" { get; init; }");
                }
            }

            builder.AppendLine("}");
        }

        private static void EmitEnumSchema(StringBuilder builder, SchemaDefinition schema)
        {
            if (schema.EnumKind == SchemaEnumKind.Integer)
            {
                EmitIntegerEnumSchema(builder, schema);
                return;
            }

            EmitStringEnumSchema(builder, schema);
        }

        private static void EmitStringEnumSchema(StringBuilder builder, SchemaDefinition schema)
        {
            EmitDocComment(
                builder,
                "    ",
                summary: schema.Summary,
                remarks: schema.Description);
            builder.Append("[JsonConverter(typeof(").Append(schema.TypeName).AppendLine("JsonConverter))]");
            builder.Append("public readonly record struct ").Append(schema.TypeName).AppendLine("(string Value)");
            builder.AppendLine("{");

            foreach (var enumMember in schema.EnumMembers)
            {
                builder.Append("    public static readonly ").Append(schema.TypeName).Append(' ').Append(enumMember.MemberName).Append(" = new(\"").Append(EscapeStringLiteral(enumMember.Value)).AppendLine("\");");
            }

            builder.AppendLine();
            builder.AppendLine("    public override string ToString() => Value;");
            builder.AppendLine("}");
            builder.AppendLine();
            builder.Append("public sealed class ").Append(schema.TypeName).Append("JsonConverter : JsonConverter<").Append(schema.TypeName).AppendLine(">");
            builder.AppendLine("{");
            builder.Append("    public override ").Append(schema.TypeName).AppendLine(" Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)");
            builder.AppendLine("    {");
            builder.Append("        return new ").Append(schema.TypeName).AppendLine("(reader.GetString()!);");
            builder.AppendLine("    }");
            builder.AppendLine();
            builder.Append("    public override void Write(Utf8JsonWriter writer, ").Append(schema.TypeName).AppendLine(" value, JsonSerializerOptions options)");
            builder.AppendLine("    {");
            builder.AppendLine("        writer.WriteStringValue(value.Value);");
            builder.AppendLine("    }");
            builder.AppendLine("}");
        }

        private static void EmitIntegerEnumSchema(StringBuilder builder, SchemaDefinition schema)
        {
            EmitDocComment(
                builder,
                "    ",
                summary: schema.Summary,
                remarks: schema.Description);
            builder.Append("public enum ").Append(schema.TypeName);

            if (!string.IsNullOrWhiteSpace(schema.EnumUnderlyingType) && !string.Equals(schema.EnumUnderlyingType, "int", StringComparison.Ordinal))
            {
                builder.Append(" : ").Append(schema.EnumUnderlyingType);
            }

            builder.AppendLine();
            builder.AppendLine("{");

            for (var i = 0; i < schema.EnumMembers.Count; i++)
            {
                var enumMember = schema.EnumMembers[i];
                builder.Append("    ").Append(enumMember.MemberName).Append(" = ").Append(enumMember.Value);
                builder.AppendLine(i < schema.EnumMembers.Count - 1 ? "," : string.Empty);
            }

            builder.AppendLine("}");
        }
    }
}
