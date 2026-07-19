using System.Globalization;
using System.Text.Json;
using System.Text.Json.Nodes;

using Microsoft.OpenApi;

namespace OpenApiWeaver.OpenApi;

internal sealed class SchemaEnumResolver(SchemaAnalysisCache cache)
{
    public bool IsEnumSchema(IOpenApiSchema schema)
    {
        return GetSchemaEnumKind(schema) != SchemaEnumKind.None;
    }

    public SchemaEnumKind GetSchemaEnumKind(IOpenApiSchema schema)
    {
        if (cache.TryGetEnumKind(schema, out var cachedKind))
        {
            return cachedKind;
        }

        var kind = CalculateSchemaEnumKind(schema);
        cache.SetEnumKind(schema, kind);
        return kind;
    }

    public static string GetNumberEnumValueType(IOpenApiSchema schema)
    {
        return string.Equals(schema.Format, "float", StringComparison.OrdinalIgnoreCase)
            ? "float"
            : string.Equals(schema.Format, "double", StringComparison.OrdinalIgnoreCase)
                ? "double"
                : "decimal";
    }

    public static string GetIntegerEnumUnderlyingType(IOpenApiSchema schema)
    {
        return string.Equals(schema.Format, "int64", StringComparison.OrdinalIgnoreCase) ? "long" : "int";
    }

    public static List<SchemaEnumMemberDefinition> CreateEnumMembers(IOpenApiSchema schema, SchemaEnumKind enumKind)
    {
        var members = new List<SchemaEnumMemberDefinition>();
        var usedMemberNames = new Dictionary<string, int>(StringComparer.Ordinal);

        foreach (var literalValue in EnumerateEnumLiteralValues(schema))
        {
            var enumValue = enumKind == SchemaEnumKind.Integer
                ? NormalizeIntegerEnumLiteral(literalValue)
                : literalValue;
            if (string.IsNullOrWhiteSpace(enumValue))
            {
                continue;
            }

            var memberName = enumKind switch
            {
                SchemaEnumKind.String => CSharpUtilities.SafeIdentifier(CSharpUtilities.ToPascalCase(enumValue)),
                SchemaEnumKind.Number => BuildNumberEnumMemberName(enumValue),
                _ => BuildIntegerEnumMemberName(enumValue)
            };

            if (!usedMemberNames.ContainsKey(memberName))
            {
                usedMemberNames[memberName] = 1;
            }
            else
            {
                usedMemberNames[memberName]++;
                memberName += usedMemberNames[memberName].ToString(CultureInfo.InvariantCulture);
            }

            members.Add(new SchemaEnumMemberDefinition(memberName, enumValue));
        }

        return members;
    }

    private static SchemaEnumKind CalculateSchemaEnumKind(IOpenApiSchema schema)
    {
        var hasEnum = schema.Enum is { Count: > 0 };
        var hasConst = !string.IsNullOrEmpty(schema.Const);
        if (!hasEnum && !hasConst)
        {
            return SchemaEnumKind.None;
        }

        if (schema.Type is { } declaredType)
        {
            var baseType = declaredType & ~JsonSchemaType.Null;
            if (baseType == JsonSchemaType.String)
            {
                return SchemaEnumKind.String;
            }

            if (baseType == JsonSchemaType.Integer)
            {
                return EnumValuesMatchKind(schema, SchemaEnumKind.Integer) ? SchemaEnumKind.Integer : SchemaEnumKind.None;
            }

            if (baseType == JsonSchemaType.Number)
            {
                return EnumValuesMatchKind(schema, SchemaEnumKind.Number) ? SchemaEnumKind.Number : SchemaEnumKind.None;
            }

            if (baseType != 0)
            {
                return SchemaEnumKind.None;
            }
        }

        return InferEnumKindFromValues(schema);
    }

    private static SchemaEnumKind InferEnumKindFromValues(IOpenApiSchema schema)
    {
        var kind = SchemaEnumKind.None;

        if (schema.Enum is { Count: > 0 } enumValues)
        {
            foreach (var node in enumValues)
            {
                var resolved = ClassifyJsonNodeEnumKind(node);
                if (resolved == SchemaEnumKind.None)
                {
                    return SchemaEnumKind.None;
                }

                if (!TryMergeInferredEnumKind(kind, resolved, out kind))
                {
                    return SchemaEnumKind.None;
                }
            }
        }

        if (!string.IsNullOrEmpty(schema.Const))
        {
            var resolved = ClassifyStringValueEnumKind(schema.Const!);
            if (resolved == SchemaEnumKind.None)
            {
                return SchemaEnumKind.None;
            }

            if (!TryMergeInferredEnumKind(kind, resolved, out kind))
            {
                return SchemaEnumKind.None;
            }
        }

        return kind;
    }

    private static SchemaEnumKind ClassifyJsonNodeEnumKind(JsonNode? node)
    {
        if (node is not JsonValue jsonValue)
        {
            return SchemaEnumKind.None;
        }

        var valueKind = jsonValue.GetValueKind();
        if (valueKind == JsonValueKind.String)
        {
            return SchemaEnumKind.String;
        }

        if (valueKind != JsonValueKind.Number)
        {
            return SchemaEnumKind.None;
        }

        var literal = node.ToString();
        return IsIntegerLiteral(literal)
            ? SchemaEnumKind.Integer
            : IsNumberLiteral(literal) ? SchemaEnumKind.Number : SchemaEnumKind.None;
    }

    private static SchemaEnumKind ClassifyStringValueEnumKind(string value)
    {
        if (IsIntegerLiteral(value))
        {
            return SchemaEnumKind.Integer;
        }

        return IsNumberLiteral(value)
            ? SchemaEnumKind.Number
            : SchemaEnumKind.String;
    }

    private static bool TryMergeInferredEnumKind(SchemaEnumKind current, SchemaEnumKind candidate, out SchemaEnumKind merged)
    {
        if (current == SchemaEnumKind.None)
        {
            merged = candidate;
            return true;
        }

        if (current == candidate)
        {
            merged = current;
            return true;
        }

        if ((current == SchemaEnumKind.Integer && candidate == SchemaEnumKind.Number)
            || (current == SchemaEnumKind.Number && candidate == SchemaEnumKind.Integer))
        {
            merged = SchemaEnumKind.Number;
            return true;
        }

        merged = SchemaEnumKind.None;
        return false;
    }

    private static bool EnumValuesMatchKind(IOpenApiSchema schema, SchemaEnumKind expectedKind)
    {
        var numberEnumValueType = expectedKind == SchemaEnumKind.Number ? GetNumberEnumValueType(schema) : null;

        foreach (var value in EnumerateEnumLiteralValues(schema))
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                continue;
            }

            if (expectedKind == SchemaEnumKind.Integer && !IsIntegerLiteral(value))
            {
                return false;
            }

            if (numberEnumValueType is not null && !CanParseNumberLiteral(value, numberEnumValueType))
            {
                return false;
            }
        }

        return true;
    }

    private static bool IsIntegerLiteral(string value)
    {
        if (long.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out _))
        {
            return true;
        }

        return decimal.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out var decimalValue)
            && decimal.Truncate(decimalValue) == decimalValue;
    }

    private static bool IsNumberLiteral(string value)
        => decimal.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out _);

    private static bool CanParseNumberLiteral(string value, string valueType)
    {
        return valueType switch
        {
            "float" => float.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out _),
            "double" => double.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out _),
            _ => decimal.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out _)
        };
    }

    private static string BuildNumberEnumMemberName(string value)
        => BuildIntegerEnumMemberName(value);

    private static string NormalizeIntegerEnumLiteral(string value)
    {
        return decimal.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out var decimalValue)
            ? decimal.Truncate(decimalValue).ToString(CultureInfo.InvariantCulture)
            : value;
    }

    private static IEnumerable<string> EnumerateEnumLiteralValues(IOpenApiSchema schema)
    {
        if (schema.Enum is { Count: > 0 } enumValues)
        {
            foreach (var item in enumValues)
            {
                var value = item?.ToString();
                if (!string.IsNullOrEmpty(value))
                {
                    yield return value!;
                }
            }
        }

        if (!string.IsNullOrEmpty(schema.Const))
        {
            yield return schema.Const!;
        }
    }

    private static string BuildIntegerEnumMemberName(string value)
    {
        var digits = value.Trim();
        if (digits.StartsWith("-", StringComparison.Ordinal))
        {
            digits = "Minus" + digits.Substring(1);
        }

        return CSharpUtilities.SafeIdentifier($"Value{digits}");
    }
}
