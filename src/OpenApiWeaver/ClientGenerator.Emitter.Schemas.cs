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

        private enum JsonSerializerDirection
        {
            Neutral,
            Request,
            Response
        }

        private string GetJsonSerializerOptionsExpression(JsonSerializerDirection direction)
        {
            if (!model.HasDirectionalSchemaProperties || direction == JsonSerializerDirection.Neutral)
            {
                return "OpenApiClientHelpers.SerializerOptions";
            }

            return direction == JsonSerializerDirection.Request
                ? $"{model.SerializerOptionsTypeName}.RequestSerializerOptions"
                : $"{model.SerializerOptionsTypeName}.ResponseSerializerOptions";
        }

        private void EmitJsonSerializerOptions(IndentedStringBuilder writer)
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
                    EmitJsonSerializerOptionsFactoryReturn(writer, "ApplyRequestSerializerMetadata");
                }

                writer.AppendLine("}");
                writer.AppendLine();
                writer.AppendLine("private static JsonSerializerOptions CreateResponseSerializerOptions()");
                writer.AppendLine("{");
                using (writer.PushIndent())
                {
                    EmitJsonSerializerOptionsFactoryReturn(writer, "ApplyResponseSerializerMetadata");
                }

                writer.AppendLine("}");
                writer.AppendLine();
                EmitJsonSerializerMetadataMethod(writer, "ApplyRequestSerializerMetadata", property => property.ReadOnly, "IgnoreSerializedProperty");
                writer.AppendLine();
                EmitJsonSerializerMetadataMethod(
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

        private static void EmitJsonSerializerOptionsFactoryReturn(IndentedStringBuilder writer, string modifierMethodName)
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

        private void EmitJsonSerializerMetadataMethod(
            IndentedStringBuilder writer,
            string methodName,
            Func<SchemaPropertyDefinition, bool> propertyPredicate,
            string configureMethodName)
            => EmitJsonSerializerMetadataMethod(writer, methodName, propertyPredicate, _ => configureMethodName);

        private void EmitJsonSerializerMetadataMethod(
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

            if (RequiresDictionaryConverter(schema))
            {
                writer.Append("[JsonConverter(typeof(").Append(schema.DeclaredTypeName).AppendLine("JsonConverter))]");
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
                    var usesPrivateInit = property.ReadOnly && !RequiresDictionaryConverter(schema);
                    var requiredModifier = property.Required && !property.ReadOnly ? "required " : string.Empty;
                    EmitDocComment(
                        writer,
                        summary: property.Summary,
                        remarks: property.Description);
                    if (property.ReadOnly)
                    {
                        writer.AppendLine("[JsonInclude]");
                    }

                    if (!property.Required && property.Type.CanBeNullInCSharp)
                    {
                        writer.AppendLine("[JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]");
                    }

                    writer.Append("[JsonPropertyName(\"").Append(EscapeStringLiteral(property.JsonPropertyName)).AppendLine("\")]");
                    var accessors = usesPrivateInit ? "{ get; private init; }" : "{ get; init; }";
                    writer.Append("public ").Append(requiredModifier).Append(property.PropertyTypeName).Append(' ').Append(property.PropertyName).Append(' ').Append(accessors).AppendLine();
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

            if (RequiresDictionaryConverter(schema))
            {
                writer.AppendLine();
                EmitDictionarySchemaConverter(writer, schema);
            }
        }

        private static bool RequiresDictionaryConverter(SchemaDefinition schema)
            => schema.DictionaryValueType is not null && schema.Properties.Count > 0;

        private void EmitDictionarySchemaConverter(IndentedStringBuilder writer, SchemaDefinition schema)
        {
            var converterName = schema.DeclaredTypeName + "JsonConverter";
            var dictionaryValueType = schema.DictionaryValueType!;

            writer.Append("public sealed class ").Append(converterName).Append(" : JsonConverter<").Append(schema.DeclaredTypeName).AppendLine(">");
            writer.AppendLine("{");
            using (writer.PushIndent())
            {
                writer.Append("public override ").Append(schema.DeclaredTypeName).AppendLine(" Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)");
                writer.AppendLine("{");
                using (writer.PushIndent())
                {
                    writer.AppendLine("if (reader.TokenType != JsonTokenType.StartObject)");
                    writer.AppendLine("{");
                    using (writer.PushIndent())
                    {
                        writer.AppendLine("throw new JsonException();");
                    }

                    writer.AppendLine("}");
                    writer.AppendLine();

                    for (var i = 0; i < schema.Properties.Count; i++)
                    {
                        var property = schema.Properties[i];
                        writer.Append(GetDictionaryConverterLocalType(property)).Append(" property").Append(i.ToString(System.Globalization.CultureInfo.InvariantCulture)).AppendLine(" = default;");
                        writer.Append("var hasProperty").Append(i.ToString(System.Globalization.CultureInfo.InvariantCulture)).AppendLine(" = false;");
                    }

                    writer.Append("var additionalProperties = new Dictionary<string, ").Append(dictionaryValueType).AppendLine(">(StringComparer.Ordinal);");
                    writer.AppendLine();
                    writer.AppendLine("while (reader.Read())");
                    writer.AppendLine("{");
                    using (writer.PushIndent())
                    {
                        writer.AppendLine("if (reader.TokenType == JsonTokenType.EndObject)");
                        writer.AppendLine("{");
                        using (writer.PushIndent())
                        {
                            writer.Append("var result = new ").Append(schema.DeclaredTypeName).AppendLine();
                            writer.AppendLine("{");
                            using (writer.PushIndent())
                            {
                                for (var i = 0; i < schema.Properties.Count; i++)
                                {
                                    var property = schema.Properties[i];
                                    writer.Append(property.PropertyName).Append(" = ");
                                    if (property.Required)
                                    {
                                        writer.Append(BuildRequiredDictionaryConverterPropertyExpression(property, i)).AppendLine(",");
                                    }
                                    else
                                    {
                                        writer.Append("property").Append(i.ToString(System.Globalization.CultureInfo.InvariantCulture)).AppendLine(",");
                                    }
                                }
                            }

                            writer.AppendLine("};");
                            writer.AppendLine();
                            writer.AppendLine("foreach (var item in additionalProperties)");
                            writer.AppendLine("{");
                            using (writer.PushIndent())
                            {
                                writer.AppendLine("result[item.Key] = item.Value;");
                            }

                            writer.AppendLine("}");
                            writer.AppendLine();
                            writer.AppendLine("return result;");
                        }

                        writer.AppendLine("}");
                        writer.AppendLine();
                        writer.AppendLine("if (reader.TokenType != JsonTokenType.PropertyName)");
                        writer.AppendLine("{");
                        using (writer.PushIndent())
                        {
                            writer.AppendLine("throw new JsonException();");
                        }

                        writer.AppendLine("}");
                        writer.AppendLine();
                        writer.AppendLine("var propertyName = reader.GetString() ?? throw new JsonException();");
                        writer.AppendLine("reader.Read();");
                        writer.AppendLine("switch (propertyName)");
                        writer.AppendLine("{");
                        using (writer.PushIndent())
                        {
                            for (var i = 0; i < schema.Properties.Count; i++)
                            {
                                var property = schema.Properties[i];
                                writer.Append("case \"").Append(EscapeStringLiteral(property.JsonPropertyName)).AppendLine("\":");
                                using (writer.PushIndent())
                                {
                                    if (property.ReadOnly || property.WriteOnly)
                                    {
                                        writer.Append("if (");
                                        var hasCondition = false;
                                        if (property.ReadOnly)
                                        {
                                            writer.Append(model.SerializerOptionsTypeName).Append(".IsRequestSerializerOptions(options)");
                                            hasCondition = true;
                                        }

                                        if (property.WriteOnly)
                                        {
                                            if (hasCondition)
                                            {
                                                writer.Append(" || ");
                                            }

                                            writer.Append(model.SerializerOptionsTypeName).Append(".IsResponseSerializerOptions(options)");
                                        }

                                        writer.AppendLine(")");
                                        writer.AppendLine("{");
                                        using (writer.PushIndent())
                                        {
                                            writer.AppendLine("reader.Skip();");
                                            writer.AppendLine("break;");
                                        }

                                        writer.AppendLine("}");
                                        writer.AppendLine();
                                    }

                                    writer.Append("property").Append(i.ToString(System.Globalization.CultureInfo.InvariantCulture))
                                        .Append(" = JsonSerializer.Deserialize<").Append(property.PropertyTypeName).AppendLine(">(ref reader, options);");
                                    writer.Append("hasProperty").Append(i.ToString(System.Globalization.CultureInfo.InvariantCulture)).AppendLine(" = true;");
                                    writer.AppendLine("break;");
                                }
                            }

                            writer.AppendLine("default:");
                            using (writer.PushIndent())
                            {
                                writer.Append("additionalProperties[propertyName] = JsonSerializer.Deserialize<").Append(dictionaryValueType).AppendLine(">(ref reader, options)!;");
                                writer.AppendLine("break;");
                            }
                        }

                        writer.AppendLine("}");
                    }

                    writer.AppendLine("}");
                    writer.AppendLine();
                    writer.AppendLine("throw new JsonException();");
                }

                writer.AppendLine("}");
                writer.AppendLine();
                writer.Append("public override void Write(Utf8JsonWriter writer, ").Append(schema.DeclaredTypeName).AppendLine(" value, JsonSerializerOptions options)");
                writer.AppendLine("{");
                using (writer.PushIndent())
                {
                    writer.AppendLine("writer.WriteStartObject();");
                    foreach (var property in schema.Properties)
                    {
                        if (!property.Required && property.Type.CanBeNullInCSharp)
                        {
                            writer.Append("if (value.").Append(property.PropertyName).AppendLine(" is not null)");
                            writer.AppendLine("{");
                            using (writer.PushIndent())
                            {
                                EmitDictionaryConverterPropertyWrite(writer, property);
                            }

                            writer.AppendLine("}");
                            continue;
                        }

                        EmitDictionaryConverterPropertyWrite(writer, property);
                    }

                    writer.AppendLine("foreach (var item in value)");
                    writer.AppendLine("{");
                    using (writer.PushIndent())
                    {
                        foreach (var property in schema.Properties)
                        {
                            writer.Append("if (string.Equals(item.Key, \"").Append(EscapeStringLiteral(property.JsonPropertyName)).AppendLine("\", StringComparison.Ordinal))");
                            writer.AppendLine("{");
                            using (writer.PushIndent())
                            {
                                writer.AppendLine("continue;");
                            }

                            writer.AppendLine("}");
                        }

                        writer.AppendLine();
                        writer.AppendLine("writer.WritePropertyName(item.Key);");
                        writer.AppendLine("JsonSerializer.Serialize(writer, item.Value, options);");
                    }

                    writer.AppendLine("}");
                    writer.AppendLine();
                    writer.AppendLine("writer.WriteEndObject();");
                }

                writer.AppendLine("}");
            }

            writer.AppendLine("}");
        }

        private void EmitDictionaryConverterPropertyWrite(IndentedStringBuilder writer, SchemaPropertyDefinition property)
        {
            if (property.ReadOnly)
            {
                writer.Append("if (!").Append(model.SerializerOptionsTypeName).AppendLine(".IsRequestSerializerOptions(options))");
                writer.AppendLine("{");
                using (writer.PushIndent())
                {
                    EmitDictionaryConverterPropertyWriteCore(writer, property);
                }

                writer.AppendLine("}");
                return;
            }

            EmitDictionaryConverterPropertyWriteCore(writer, property);
        }

        private static void EmitDictionaryConverterPropertyWriteCore(IndentedStringBuilder writer, SchemaPropertyDefinition property)
        {
            writer.Append("writer.WritePropertyName(\"").Append(EscapeStringLiteral(property.JsonPropertyName)).AppendLine("\");");
            writer.Append("JsonSerializer.Serialize(writer, value.").Append(property.PropertyName).AppendLine(", options);");
        }

        private static string GetDictionaryConverterLocalType(SchemaPropertyDefinition property)
        {
            return !property.Type.CanBeNullInCSharp && IsReferenceLikeType(property.Type.Shape)
                ? CSharpUtilities.MakeNullableTypeName(property.PropertyTypeName)
                : property.PropertyTypeName;
        }

        private string BuildRequiredDictionaryConverterPropertyExpression(SchemaPropertyDefinition property, int index)
        {
            var propertyVariable = $"property{index.ToString(System.Globalization.CultureInfo.InvariantCulture)}";
            var missingExpression = $"throw new JsonException(\"Required property '{EscapeStringLiteral(property.JsonPropertyName)}' was not found.\")";
            var optionalInDirectionExpression = property switch
            {
                { ReadOnly: true, WriteOnly: true } => $"{model.SerializerOptionsTypeName}.IsRequestSerializerOptions(options) || {model.SerializerOptionsTypeName}.IsResponseSerializerOptions(options)",
                { ReadOnly: true } => $"{model.SerializerOptionsTypeName}.IsRequestSerializerOptions(options)",
                { WriteOnly: true } => $"{model.SerializerOptionsTypeName}.IsResponseSerializerOptions(options)",
                _ => null
            };
            if (optionalInDirectionExpression is not null)
            {
                missingExpression = $"{optionalInDirectionExpression} ? default! : {missingExpression}";
            }

            if (!property.Type.CanBeNullInCSharp && IsReferenceLikeType(property.Type.Shape))
            {
                var nullExpression = $"throw new JsonException(\"Required property '{EscapeStringLiteral(property.JsonPropertyName)}' was null.\")";
                return $"hasProperty{index.ToString(System.Globalization.CultureInfo.InvariantCulture)} ? {propertyVariable} ?? {nullExpression} : {missingExpression}";
            }

            return $"hasProperty{index.ToString(System.Globalization.CultureInfo.InvariantCulture)} ? {propertyVariable} : {missingExpression}";
        }

        private static bool IsReferenceLikeType(TypeShape shape)
            => shape is TypeShape.String or TypeShape.Object or TypeShape.Array or TypeShape.Dictionary or TypeShape.Binary;

        private static void EmitEnumSchema(IndentedStringBuilder writer, SchemaDefinition schema)
        {
            if (schema.EnumKind == SchemaEnumKind.Integer)
            {
                EmitIntegerEnumSchema(writer, schema);
                return;
            }

            if (schema.EnumKind == SchemaEnumKind.Number)
            {
                EmitNumberEnumSchema(writer, schema);
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

        private static void EmitNumberEnumSchema(IndentedStringBuilder writer, SchemaDefinition schema)
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
}
