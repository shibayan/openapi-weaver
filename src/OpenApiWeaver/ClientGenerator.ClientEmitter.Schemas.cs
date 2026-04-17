namespace OpenApiWeaver;

public sealed partial class ClientGenerator
{
    private sealed partial class ClientEmitter
    {
        private Dictionary<string, List<SchemaDefinition>> _nestedSchemasByParent = null!;

        private void EmitSchemas(IndentedStringBuilder writer)
        {
            _nestedSchemasByParent = new Dictionary<string, List<SchemaDefinition>>(StringComparer.Ordinal);
            foreach (var schema in model.Schemas)
            {
                if (schema.ParentTypeName is null)
                {
                    continue;
                }

                if (!_nestedSchemasByParent.TryGetValue(schema.ParentTypeName, out var children))
                {
                    children = [];
                    _nestedSchemasByParent[schema.ParentTypeName] = children;
                }

                children.Add(schema);
            }

            foreach (var schema in model.Schemas)
            {
                if (schema.ParentTypeName is not null)
                {
                    continue;
                }

                EmitSchemaDefinition(writer, schema);
                writer.AppendLine();
            }
        }

        private void EmitSchemaDefinition(IndentedStringBuilder writer, SchemaDefinition schema)
        {
            if (schema.IsEnum)
            {
                EmitEnumSchema(writer, schema);
                return;
            }

            EmitSchema(writer, schema);
        }

        private void EmitSchema(IndentedStringBuilder writer, SchemaDefinition schema)
        {
            EmitDocComment(
                writer,
                summary: schema.Summary,
                remarks: schema.Description);
            if (schema.DictionaryValueType is not null)
            {
                writer.Append("public sealed class ").Append(schema.DeclaredTypeName).Append(" : Dictionary<string, ").Append(schema.DictionaryValueType).AppendLine(">");
            }
            else
            {
                writer.Append("public sealed class ").Append(schema.DeclaredTypeName).AppendLine();
            }

            writer.AppendLine("{");
            using (writer.PushIndent())
            {
                foreach (var property in schema.Properties)
                {
                    var requiredModifier = property.Required ? "required " : string.Empty;
                    EmitDocComment(
                        writer,
                        summary: property.Summary,
                        remarks: property.Description);
                    if (!property.Required && property.TypeName.EndsWith("?", StringComparison.Ordinal))
                    {
                        writer.AppendLine("[JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]");
                    }

                    writer.Append("[JsonPropertyName(\"").Append(EscapeStringLiteral(property.JsonName)).AppendLine("\")]");
                    writer.Append("public ").Append(requiredModifier).Append(property.TypeName).Append(' ').Append(property.PropertyName).AppendLine(" { get; init; }");
                }

                if (_nestedSchemasByParent.TryGetValue(schema.TypeName, out var nestedSchemas))
                {
                    foreach (var nestedSchema in nestedSchemas)
                    {
                        writer.AppendLine();
                        EmitSchemaDefinition(writer, nestedSchema);
                    }
                }
            }

            writer.AppendLine("}");
        }

        private static void EmitEnumSchema(IndentedStringBuilder writer, SchemaDefinition schema)
        {
            if (schema.EnumKind == SchemaEnumKind.Integer)
            {
                EmitIntegerEnumSchema(writer, schema);
                return;
            }

            EmitStringEnumSchema(writer, schema);
        }

        private static void EmitStringEnumSchema(IndentedStringBuilder writer, SchemaDefinition schema)
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

        private static void EmitIntegerEnumSchema(IndentedStringBuilder writer, SchemaDefinition schema)
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
    }
}
