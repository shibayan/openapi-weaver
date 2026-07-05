using static OpenApiWeaver.CodeGeneration.CSharpCodeEmissionUtilities;

namespace OpenApiWeaver.CodeGeneration;

internal static class DictionarySchemaConverterEmitter
{
    public static bool RequiresConverter(SchemaDefinition schema)
        => schema.DictionaryValueType is not null && schema.Properties.Count > 0;

    public static void Emit(IndentedStringBuilder writer, SchemaDefinition schema, string serializerOptionsTypeName)
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
                    writer.Append(GetLocalType(property)).Append(" property").Append(i.ToString(System.Globalization.CultureInfo.InvariantCulture)).AppendLine(" = default;");
                    if (property.Required)
                    {
                        writer.Append("var hasProperty").Append(i.ToString(System.Globalization.CultureInfo.InvariantCulture)).AppendLine(" = false;");
                    }
                }

                writer.Append("var additionalProperties = new Dictionary<string, ").Append(dictionaryValueType).AppendLine(">(StringComparer.Ordinal);");
                writer.AppendLine();
                writer.AppendLine("while (reader.Read())");
                writer.AppendLine("{");
                using (writer.PushIndent())
                {
                    EmitReadLoopBody(writer, schema, serializerOptionsTypeName, dictionaryValueType);
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
                EmitWriteBody(writer, schema, serializerOptionsTypeName);
            }

            writer.AppendLine("}");
        }

        writer.AppendLine("}");
    }

    private static void EmitReadLoopBody(
        IndentedStringBuilder writer,
        SchemaDefinition schema,
        string serializerOptionsTypeName,
        string dictionaryValueType)
    {
        writer.AppendLine("if (reader.TokenType == JsonTokenType.EndObject)");
        writer.AppendLine("{");
        using (writer.PushIndent())
        {
            EmitResultReturn(writer, schema, serializerOptionsTypeName);
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
                    EmitKnownPropertyRead(writer, property, i, serializerOptionsTypeName);
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

    private static void EmitResultReturn(IndentedStringBuilder writer, SchemaDefinition schema, string serializerOptionsTypeName)
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
                    writer.Append(BuildRequiredPropertyExpression(property, i, serializerOptionsTypeName)).AppendLine(",");
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

    private static void EmitKnownPropertyRead(
        IndentedStringBuilder writer,
        SchemaPropertyDefinition property,
        int index,
        string serializerOptionsTypeName)
    {
        if (property.ReadOnly || property.WriteOnly)
        {
            writer.Append("if (");
            var hasCondition = false;
            if (property.ReadOnly)
            {
                writer.Append(serializerOptionsTypeName).Append(".IsRequestSerializerOptions(options)");
                hasCondition = true;
            }

            if (property.WriteOnly)
            {
                if (hasCondition)
                {
                    writer.Append(" || ");
                }

                writer.Append(serializerOptionsTypeName).Append(".IsResponseSerializerOptions(options)");
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

        writer.Append("property").Append(index.ToString(System.Globalization.CultureInfo.InvariantCulture))
            .Append(" = JsonSerializer.Deserialize<").Append(property.PropertyTypeName).AppendLine(">(ref reader, options);");
        if (property.Required)
        {
            writer.Append("hasProperty").Append(index.ToString(System.Globalization.CultureInfo.InvariantCulture)).AppendLine(" = true;");
        }

        writer.AppendLine("break;");
    }

    private static void EmitWriteBody(IndentedStringBuilder writer, SchemaDefinition schema, string serializerOptionsTypeName)
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
                    EmitPropertyWrite(writer, property, serializerOptionsTypeName);
                }

                writer.AppendLine("}");
                continue;
            }

            EmitPropertyWrite(writer, property, serializerOptionsTypeName);
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

    private static void EmitPropertyWrite(
        IndentedStringBuilder writer,
        SchemaPropertyDefinition property,
        string serializerOptionsTypeName)
    {
        if (property.ReadOnly)
        {
            writer.Append("if (!").Append(serializerOptionsTypeName).AppendLine(".IsRequestSerializerOptions(options))");
            writer.AppendLine("{");
            using (writer.PushIndent())
            {
                EmitPropertyWriteCore(writer, property);
            }

            writer.AppendLine("}");
            return;
        }

        EmitPropertyWriteCore(writer, property);
    }

    private static void EmitPropertyWriteCore(IndentedStringBuilder writer, SchemaPropertyDefinition property)
    {
        writer.Append("writer.WritePropertyName(\"").Append(EscapeStringLiteral(property.JsonPropertyName)).AppendLine("\");");
        writer.Append("JsonSerializer.Serialize(writer, value.").Append(property.PropertyName).AppendLine(", options);");
    }

    private static string GetLocalType(SchemaPropertyDefinition property)
    {
        return !property.Type.CanBeNullInCSharp && IsReferenceLikeType(property.Type.Shape)
            ? CSharpUtilities.MakeNullableTypeName(property.PropertyTypeName)
            : property.PropertyTypeName;
    }

    private static string BuildRequiredPropertyExpression(
        SchemaPropertyDefinition property,
        int index,
        string serializerOptionsTypeName)
    {
        var propertyVariable = $"property{index.ToString(System.Globalization.CultureInfo.InvariantCulture)}";
        var missingExpression = $"throw new JsonException(\"Required property '{EscapeStringLiteral(property.JsonPropertyName)}' was not found.\")";
        var optionalInDirectionExpression = property switch
        {
            { ReadOnly: true, WriteOnly: true } => $"{serializerOptionsTypeName}.IsRequestSerializerOptions(options) || {serializerOptionsTypeName}.IsResponseSerializerOptions(options)",
            { ReadOnly: true } => $"{serializerOptionsTypeName}.IsRequestSerializerOptions(options)",
            { WriteOnly: true } => $"{serializerOptionsTypeName}.IsResponseSerializerOptions(options)",
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
}
