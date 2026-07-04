using static OpenApiWeaver.CodeGeneration.CSharpCodeEmissionUtilities;

namespace OpenApiWeaver.CodeGeneration;

internal static class EnumSchemaEmitter
{
    public static void Emit(IndentedStringBuilder writer, SchemaDefinition schema)
    {
        if (schema.EnumKind == SchemaEnumKind.Integer)
        {
            EmitIntegerSchema(writer, schema);
            return;
        }

        if (schema.EnumKind == SchemaEnumKind.Number)
        {
            EmitNumberSchema(writer, schema);
            return;
        }

        EmitStringSchema(writer, schema);
    }

    private static void EmitStringSchema(IndentedStringBuilder writer, SchemaDefinition schema)
    {
        EmitDocComment(
            writer,
            summary: schema.Summary,
            remarks: schema.Description);
        writer.Append("[JsonConverter(typeof(").Append(schema.DeclaredTypeName).AppendLine("JsonConverter))]");
        writer.Append("public readonly record struct ").Append(schema.DeclaredTypeName).AppendLine("(string Value)");
        writer.AppendLine("{");
        using (writer.PushIndent())
        {
            foreach (var enumMember in schema.EnumMembers)
            {
                writer.Append("public static readonly ").Append(schema.DeclaredTypeName).Append(' ').Append(enumMember.MemberName).Append(" = new(\"").Append(EscapeStringLiteral(enumMember.Value)).AppendLine("\");");
            }

            writer.AppendLine();
            writer.AppendLine("public override string ToString() => Value;");
        }

        writer.AppendLine("}");
        writer.AppendLine();
        writer.Append("public sealed class ").Append(schema.DeclaredTypeName).Append("JsonConverter : JsonConverter<").Append(schema.DeclaredTypeName).AppendLine(">");
        writer.AppendLine("{");
        using (writer.PushIndent())
        {
            writer.Append("public override ").Append(schema.DeclaredTypeName).AppendLine(" Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)");
            writer.AppendLine("{");
            using (writer.PushIndent())
            {
                writer.Append("return new ").Append(schema.DeclaredTypeName).AppendLine("(reader.GetString()!);");
            }

            writer.AppendLine("}");
            writer.AppendLine();
            writer.Append("public override void Write(Utf8JsonWriter writer, ").Append(schema.DeclaredTypeName).AppendLine(" value, JsonSerializerOptions options)");
            writer.AppendLine("{");
            using (writer.PushIndent())
            {
                writer.AppendLine("writer.WriteStringValue(value.Value);");
            }

            writer.AppendLine("}");
        }

        writer.AppendLine("}");
    }

    private static void EmitNumberSchema(IndentedStringBuilder writer, SchemaDefinition schema)
    {
        var valueType = schema.EnumUnderlyingType ?? "decimal";
        EmitDocComment(
            writer,
            summary: schema.Summary,
            remarks: schema.Description);
        writer.Append("[JsonConverter(typeof(").Append(schema.DeclaredTypeName).AppendLine("JsonConverter))]");
        writer.Append("public readonly record struct ").Append(schema.DeclaredTypeName).Append('(').Append(valueType).AppendLine(" Value)");
        writer.AppendLine("{");
        using (writer.PushIndent())
        {
            foreach (var enumMember in schema.EnumMembers)
            {
                writer.Append("public static readonly ").Append(schema.DeclaredTypeName).Append(' ').Append(enumMember.MemberName).Append(" = new(")
                    .Append(FormatNumberLiteral(enumMember.Value, valueType)).AppendLine(");");
            }

            writer.AppendLine();
            writer.AppendLine("public override string ToString() => Value.ToString(System.Globalization.CultureInfo.InvariantCulture);");
        }

        writer.AppendLine("}");
        writer.AppendLine();
        writer.Append("public sealed class ").Append(schema.DeclaredTypeName).Append("JsonConverter : JsonConverter<").Append(schema.DeclaredTypeName).AppendLine(">");
        writer.AppendLine("{");
        using (writer.PushIndent())
        {
            writer.Append("public override ").Append(schema.DeclaredTypeName).AppendLine(" Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)");
            writer.AppendLine("{");
            using (writer.PushIndent())
            {
                writer.Append("return new ").Append(schema.DeclaredTypeName).Append("(reader.").Append(GetJsonReaderNumberMethod(valueType)).AppendLine("());");
            }

            writer.AppendLine("}");
            writer.AppendLine();
            writer.Append("public override void Write(Utf8JsonWriter writer, ").Append(schema.DeclaredTypeName).AppendLine(" value, JsonSerializerOptions options)");
            writer.AppendLine("{");
            using (writer.PushIndent())
            {
                writer.AppendLine("writer.WriteNumberValue(value.Value);");
            }

            writer.AppendLine("}");
        }

        writer.AppendLine("}");
    }

    private static void EmitIntegerSchema(IndentedStringBuilder writer, SchemaDefinition schema)
    {
        EmitDocComment(
            writer,
            summary: schema.Summary,
            remarks: schema.Description);
        writer.Append("public enum ").Append(schema.DeclaredTypeName);

        if (!string.IsNullOrWhiteSpace(schema.EnumUnderlyingType) && !string.Equals(schema.EnumUnderlyingType, "int", StringComparison.Ordinal))
        {
            writer.Append(" : ").Append(schema.EnumUnderlyingType);
        }

        writer.AppendLine();
        writer.AppendLine("{");
        using (writer.PushIndent())
        {
            for (var i = 0; i < schema.EnumMembers.Count; i++)
            {
                var enumMember = schema.EnumMembers[i];
                writer.Append(enumMember.MemberName).Append(" = ").Append(enumMember.Value);
                writer.AppendLine(i < schema.EnumMembers.Count - 1 ? "," : string.Empty);
            }
        }

        writer.AppendLine("}");
    }

    private static string FormatNumberLiteral(string value, string valueType)
    {
        var suffix = valueType switch
        {
            "float" => "f",
            "double" => "d",
            _ => "m"
        };
        return value.Trim() + suffix;
    }

    private static string GetJsonReaderNumberMethod(string valueType)
    {
        return valueType switch
        {
            "float" => "GetSingle",
            "double" => "GetDouble",
            _ => "GetDecimal"
        };
    }
}
