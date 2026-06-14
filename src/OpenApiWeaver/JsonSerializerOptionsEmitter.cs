using static OpenApiWeaver.CSharpCodeEmissionUtilities;

namespace OpenApiWeaver;

internal sealed class JsonSerializerOptionsEmitter(ClientModel model)
{
    public string GetOptionsExpression(JsonSerializerDirection direction)
    {
        if (!model.HasDirectionalSchemaProperties || direction == JsonSerializerDirection.Neutral)
        {
            return "OpenApiClientHelpers.SerializerOptions";
        }

        return direction == JsonSerializerDirection.Request
            ? $"{model.SerializerOptionsTypeName}.RequestSerializerOptions"
            : $"{model.SerializerOptionsTypeName}.ResponseSerializerOptions";
    }

    public void Emit(IndentedStringBuilder writer)
    {
        writer.Append("internal static class ").Append(model.SerializerOptionsTypeName).AppendLine();
        writer.AppendLine("{");
        using (writer.PushIndent())
        {
            writer.AppendLine("internal static readonly JsonSerializerOptions RequestSerializerOptions = CreateRequestSerializerOptions();");
            writer.AppendLine();
            writer.AppendLine("internal static readonly JsonSerializerOptions ResponseSerializerOptions = CreateResponseSerializerOptions();");
            writer.AppendLine();
            writer.AppendLine("internal static bool IsRequestSerializerOptions(JsonSerializerOptions options)");
            using (writer.PushIndent())
            {
                writer.AppendLine("=> ReferenceEquals(options, RequestSerializerOptions);");
            }

            writer.AppendLine();
            writer.AppendLine("internal static bool IsResponseSerializerOptions(JsonSerializerOptions options)");
            using (writer.PushIndent())
            {
                writer.AppendLine("=> ReferenceEquals(options, ResponseSerializerOptions);");
            }

            writer.AppendLine();
            writer.AppendLine("private static JsonSerializerOptions CreateRequestSerializerOptions()");
            writer.AppendLine("{");
            using (writer.PushIndent())
            {
                EmitFactoryReturn(writer, "ApplyRequestSerializerMetadata");
            }

            writer.AppendLine("}");
            writer.AppendLine();
            writer.AppendLine("private static JsonSerializerOptions CreateResponseSerializerOptions()");
            writer.AppendLine("{");
            using (writer.PushIndent())
            {
                EmitFactoryReturn(writer, "ApplyResponseSerializerMetadata");
            }

            writer.AppendLine("}");
            writer.AppendLine();
            EmitMetadataMethod(writer, "ApplyRequestSerializerMetadata", property => property.ReadOnly, "IgnoreSerializedProperty");
            writer.AppendLine();
            EmitMetadataMethod(
                writer,
                "ApplyResponseSerializerMetadata",
                property => property.WriteOnly || (property.ReadOnly && property.Required),
                static property => property.WriteOnly ? "IgnoreDeserializedProperty" : "RequireDeserializedProperty");
            writer.AppendLine();
            writer.AppendLine("private static void IgnoreSerializedProperty(System.Text.Json.Serialization.Metadata.JsonTypeInfo typeInfo, string propertyName)");
            writer.AppendLine("{");
            using (writer.PushIndent())
            {
                writer.AppendLine("foreach (var property in typeInfo.Properties)");
                writer.AppendLine("{");
                using (writer.PushIndent())
                {
                    writer.AppendLine("if (!string.Equals(property.Name, propertyName, StringComparison.Ordinal))");
                    writer.AppendLine("{");
                    using (writer.PushIndent())
                    {
                        writer.AppendLine("continue;");
                    }

                    writer.AppendLine("}");
                    writer.AppendLine();
                    writer.AppendLine("property.ShouldSerialize = static (_, _) => false;");
                    writer.AppendLine("property.IsRequired = false;");
                    writer.AppendLine("return;");
                }

                writer.AppendLine("}");
            }

            writer.AppendLine("}");
            writer.AppendLine();
            writer.AppendLine("private static void RequireDeserializedProperty(System.Text.Json.Serialization.Metadata.JsonTypeInfo typeInfo, string propertyName)");
            writer.AppendLine("{");
            using (writer.PushIndent())
            {
                writer.AppendLine("foreach (var property in typeInfo.Properties)");
                writer.AppendLine("{");
                using (writer.PushIndent())
                {
                    writer.AppendLine("if (!string.Equals(property.Name, propertyName, StringComparison.Ordinal))");
                    writer.AppendLine("{");
                    using (writer.PushIndent())
                    {
                        writer.AppendLine("continue;");
                    }

                    writer.AppendLine("}");
                    writer.AppendLine();
                    writer.AppendLine("property.IsRequired = true;");
                    writer.AppendLine("return;");
                }

                writer.AppendLine("}");
            }

            writer.AppendLine("}");
            writer.AppendLine();
            writer.AppendLine("private static void IgnoreDeserializedProperty(System.Text.Json.Serialization.Metadata.JsonTypeInfo typeInfo, string propertyName)");
            writer.AppendLine("{");
            using (writer.PushIndent())
            {
                writer.AppendLine("foreach (var property in typeInfo.Properties)");
                writer.AppendLine("{");
                using (writer.PushIndent())
                {
                    writer.AppendLine("if (!string.Equals(property.Name, propertyName, StringComparison.Ordinal))");
                    writer.AppendLine("{");
                    using (writer.PushIndent())
                    {
                        writer.AppendLine("continue;");
                    }

                    writer.AppendLine("}");
                    writer.AppendLine();
                    writer.AppendLine("property.Set = null;");
                    writer.AppendLine("property.IsRequired = false;");
                    writer.AppendLine("return;");
                }

                writer.AppendLine("}");
            }

            writer.AppendLine("}");
        }

        writer.AppendLine("}");
    }

    private static void EmitFactoryReturn(IndentedStringBuilder writer, string modifierMethodName)
    {
        writer.AppendLine("return new JsonSerializerOptions(JsonSerializerDefaults.Web)");
        writer.AppendLine("{");
        using (writer.PushIndent())
        {
            writer.AppendLine("TypeInfoResolver = new System.Text.Json.Serialization.Metadata.DefaultJsonTypeInfoResolver");
            writer.AppendLine("{");
            using (writer.PushIndent())
            {
                writer.AppendLine("Modifiers =");
                writer.AppendLine("{");
                using (writer.PushIndent())
                {
                    writer.Append(modifierMethodName).AppendLine();
                }

                writer.AppendLine("}");
            }

            writer.AppendLine("}");
        }

        writer.AppendLine("};");
    }

    private void EmitMetadataMethod(
        IndentedStringBuilder writer,
        string methodName,
        Func<SchemaPropertyDefinition, bool> propertyPredicate,
        string configureMethodName)
        => EmitMetadataMethod(writer, methodName, propertyPredicate, _ => configureMethodName);

    private void EmitMetadataMethod(
        IndentedStringBuilder writer,
        string methodName,
        Func<SchemaPropertyDefinition, bool> propertyPredicate,
        Func<SchemaPropertyDefinition, string> configureMethodNameSelector)
    {
        writer.Append("private static void ").Append(methodName).AppendLine("(System.Text.Json.Serialization.Metadata.JsonTypeInfo typeInfo)");
        writer.AppendLine("{");
        using (writer.PushIndent())
        {
            foreach (var schema in model.Schemas)
            {
                var properties = schema.Properties.Where(propertyPredicate).ToList();
                if (properties.Count == 0)
                {
                    continue;
                }

                writer.Append("if (typeInfo.Type == typeof(").Append(schema.QualifiedTypeName).AppendLine("))");
                writer.AppendLine("{");
                using (writer.PushIndent())
                {
                    foreach (var property in properties)
                    {
                        writer.Append(configureMethodNameSelector(property)).Append("(typeInfo, \"").Append(EscapeStringLiteral(property.JsonPropertyName)).AppendLine("\");");
                    }
                }

                writer.AppendLine("}");
            }
        }

        writer.AppendLine("}");
    }
}
