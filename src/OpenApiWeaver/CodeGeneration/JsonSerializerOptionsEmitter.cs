using static OpenApiWeaver.CodeGeneration.CSharpCodeEmissionUtilities;

namespace OpenApiWeaver.CodeGeneration;

internal sealed class JsonSerializerOptionsEmitter(ClientModel model)
{
    public static string GetOptionsExpression(ClientModel model, JsonSerializerDirection direction)
    {
        if (!model.HasSerializerMetadata)
        {
            return SupportTypeNames.DefaultSerializerOptionsExpression;
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
            EmitMetadataMethod(
                writer,
                "ApplyRequestSerializerMetadata",
                property => property.ReadOnly,
                static _ => "IgnoreSerializedProperty",
                static kind => kind == InheritedPropertyRuleKind.IgnoreInRequest ? "IgnoreSerializedProperty" : null);
            writer.AppendLine();
            EmitMetadataMethod(
                writer,
                "ApplyResponseSerializerMetadata",
                property => property.WriteOnly || (property.ReadOnly && property.Required),
                static property => property.WriteOnly ? "IgnoreDeserializedProperty" : "RequireDeserializedProperty",
                static kind => kind switch
                {
                    InheritedPropertyRuleKind.RequireInResponse => "RequireDeserializedProperty",
                    InheritedPropertyRuleKind.IgnoreInResponse => "IgnoreDeserializedProperty",
                    _ => null
                });
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
        writer.Append("var options = ").Append(SupportTypeNames.ClientHelpers).AppendLine(".CreateSerializerOptions();");
        writer.AppendLine("options.TypeInfoResolver = new System.Text.Json.Serialization.Metadata.DefaultJsonTypeInfoResolver");
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

        writer.AppendLine("};");
        writer.AppendLine("return options;");
    }

    private void EmitMetadataMethod(
        IndentedStringBuilder writer,
        string methodName,
        Func<SchemaPropertyDefinition, bool> propertyPredicate,
        Func<SchemaPropertyDefinition, string> configureMethodNameSelector,
        Func<InheritedPropertyRuleKind, string?> inheritedRuleMethodNameSelector)
    {
        writer.Append("private static void ").Append(methodName).AppendLine("(System.Text.Json.Serialization.Metadata.JsonTypeInfo typeInfo)");
        writer.AppendLine("{");
        using (writer.PushIndent())
        {
            // Ancestors' rules run first so a subtype's own rules (e.g. making an inherited property writeOnly) win,
            // regardless of the order components are declared in.
            var schemasByTypeName = model.Schemas.ToDictionary(static schema => schema.QualifiedTypeName, StringComparer.Ordinal);
            int GetDepth(SchemaDefinition schema)
            {
                var depth = 0;
                while (schema.BaseTypeName is { } baseTypeName && schemasByTypeName.TryGetValue(baseTypeName, out var baseSchema))
                {
                    depth++;
                    schema = baseSchema;
                }

                return depth;
            }

            foreach (var schema in model.Schemas.OrderBy(GetDepth))
            {
                var rules = schema.Properties
                    .Where(propertyPredicate)
                    .Select(property => (MethodName: configureMethodNameSelector(property), property.JsonPropertyName))
                    .ToList();
                foreach (var inheritedRule in schema.InheritedPropertyRules)
                {
                    if (inheritedRuleMethodNameSelector(inheritedRule.Kind) is { } inheritedMethodName)
                    {
                        rules.Add((inheritedMethodName, inheritedRule.JsonPropertyName));
                    }
                }

                if (rules.Count == 0)
                {
                    continue;
                }

                // IsAssignableFrom applies a base model's rules to the contracts of its derived models too.
                writer.Append("if (typeof(").Append(schema.QualifiedTypeName).AppendLine(").IsAssignableFrom(typeInfo.Type))");
                writer.AppendLine("{");
                using (writer.PushIndent())
                {
                    foreach (var (configureMethodName, jsonPropertyName) in rules)
                    {
                        writer.Append(configureMethodName).Append("(typeInfo, \"").Append(EscapeStringLiteral(jsonPropertyName)).AppendLine("\");");
                    }
                }

                writer.AppendLine("}");
            }
        }

        writer.AppendLine("}");
    }
}
