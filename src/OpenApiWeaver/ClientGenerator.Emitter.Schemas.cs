namespace OpenApiWeaver;

public sealed partial class ClientGenerator
{
    private sealed partial class Emitter
    {
        private readonly Dictionary<string, List<SchemaDefinition>> _nestedSchemasByParent = BuildNestedSchemaLookup(model.Schemas);

        private static Dictionary<string, List<SchemaDefinition>> BuildNestedSchemaLookup(IReadOnlyList<SchemaDefinition> schemas)
        {
            var lookup = new Dictionary<string, List<SchemaDefinition>>(StringComparer.Ordinal);
            foreach (var schema in schemas)
            {
                if (schema.ParentTypeName is null)
                {
                    continue;
                }

                if (!lookup.TryGetValue(schema.ParentTypeName, out var children))
                {
                    children = [];
                    lookup[schema.ParentTypeName] = children;
                }

                children.Add(schema);
            }

            return lookup;
        }

        private void EmitSchemas(IndentedStringBuilder writer)
        {
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

            if (schema.IsPolymorphicBase)
            {
                writer.Append("[JsonPolymorphic(TypeDiscriminatorPropertyName = \"").Append(EscapeStringLiteral(schema.DiscriminatorPropertyName!)).AppendLine("\")]");
                foreach (var derivedType in schema.DerivedTypes)
                {
                    writer.Append("[JsonDerivedType(typeof(").Append(derivedType.TypeName).Append("), typeDiscriminator: \"").Append(EscapeStringLiteral(derivedType.DiscriminatorValue)).AppendLine("\")]");
                }
            }

            if (schema.DictionaryValueType is not null)
            {
                writer.Append("public sealed class ").Append(schema.DeclaredTypeName).Append(" : Dictionary<string, ").Append(schema.DictionaryValueType).AppendLine(">");
            }
            else
            {
                writer.Append(schema.IsPolymorphicBase ? "public class " : "public sealed class ").Append(schema.DeclaredTypeName);
                if (!string.IsNullOrWhiteSpace(schema.BaseTypeName))
                {
                    writer.Append(" : ").Append(schema.BaseTypeName);
                }

                writer.AppendLine();
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
                    if (!property.Required && property.Type.CanBeNullInCSharp)
                    {
                        writer.AppendLine("[JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]");
                    }

                    writer.Append("[JsonPropertyName(\"").Append(EscapeStringLiteral(property.JsonPropertyName)).AppendLine("\")]");
                    writer.Append("public ").Append(requiredModifier).Append(property.PropertyTypeName).Append(' ').Append(property.PropertyName).AppendLine(" { get; init; }");
                }

                if (_nestedSchemasByParent.TryGetValue(schema.QualifiedTypeName, out var nestedSchemas))
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
