using System.Globalization;
using System.Text.Json;
using System.Text.Json.Nodes;

using Microsoft.OpenApi;

namespace OpenApiWeaver;

internal sealed class SchemaTypeResolver(OpenApiDocument document, SchemaCatalog catalog, SchemaAnalysisCache cache)
{
    public TypeUsage ResolveTypeUsage(IOpenApiSchema? schema, bool required)
    {
        if (schema is null)
        {
            return new TypeUsage("string", TypeShape.String, schemaAllowsNull: false, isOptional: !required);
        }

        if (cache.TryGetTypeUsage(schema, required, out var cachedTypeUsage))
        {
            return cachedTypeUsage;
        }

        var typeUsage = CreateTypeUsage(schema, required);
        cache.SetTypeUsage(schema, required, typeUsage);
        return typeUsage;
    }

    private TypeUsage CreateTypeUsage(IOpenApiSchema schema, bool required)
    {
        if (schema is IOpenApiReferenceHolder<JsonSchemaReference> { Reference.Id: not null } referenceHolder
            && catalog.TryGetComponentSchemaName(referenceHolder.Reference.Id, out var schemaName))
        {
            return new TypeUsage(
                schemaName,
                GetTypeShape(ResolveSchemaReference(schema)),
                SchemaAllowsNull(schema),
                isOptional: !required);
        }

        if (catalog.TryGetInlineSchema(GetSchemaIdentity(schema), out var inlineSchema))
        {
            return new TypeUsage(
                inlineSchema.TypeName,
                GetTypeShape(schema),
                SchemaAllowsNull(schema),
                isOptional: !required);
        }

        if (TryResolveCompositeTypeUsage(schema, required, out var compositeType))
        {
            return compositeType;
        }

        if (TryGetDictionaryValueType(schema, out var dictionaryValueType))
        {
            var dictionaryType = $"IReadOnlyDictionary<string, {dictionaryValueType}>";
            return new TypeUsage(
                dictionaryType,
                TypeShape.Dictionary,
                SchemaAllowsNull(schema),
                isOptional: !required);
        }

        var resolvedSchema = ResolveSchemaReference(schema);
        var baseType = resolvedSchema.Type & ~JsonSchemaType.Null;
        var (typeName, typeShape) = baseType switch
        {
            JsonSchemaType.Integer when string.Equals(resolvedSchema.Format, "int64", StringComparison.OrdinalIgnoreCase) => ("long", TypeShape.Primitive),
            JsonSchemaType.Integer => ("int", TypeShape.Primitive),
            JsonSchemaType.Number when string.Equals(resolvedSchema.Format, "float", StringComparison.OrdinalIgnoreCase) => ("float", TypeShape.Primitive),
            JsonSchemaType.Number when string.Equals(resolvedSchema.Format, "double", StringComparison.OrdinalIgnoreCase) => ("double", TypeShape.Primitive),
            JsonSchemaType.Number when string.Equals(resolvedSchema.Format, "decimal", StringComparison.OrdinalIgnoreCase) => ("decimal", TypeShape.Primitive),
            JsonSchemaType.Number => ("decimal", TypeShape.Primitive),
            JsonSchemaType.Boolean => ("bool", TypeShape.Primitive),
            JsonSchemaType.String when string.Equals(resolvedSchema.Format, "date", StringComparison.OrdinalIgnoreCase) => ("DateOnly", TypeShape.Primitive),
            JsonSchemaType.String when string.Equals(resolvedSchema.Format, "date-time", StringComparison.OrdinalIgnoreCase) => ("DateTimeOffset", TypeShape.Primitive),
            JsonSchemaType.String when string.Equals(resolvedSchema.Format, "uuid", StringComparison.OrdinalIgnoreCase) => ("Guid", TypeShape.Primitive),
            JsonSchemaType.String when string.Equals(resolvedSchema.Format, "binary", StringComparison.OrdinalIgnoreCase) => ("byte[]", TypeShape.Binary),
            JsonSchemaType.Array => ($"IReadOnlyList<{ResolveTypeUsage(resolvedSchema.Items, required: true).CSharpTypeName}>", TypeShape.Array),
            JsonSchemaType.String => ("string", TypeShape.String),
            _ => ("JsonElement", TypeShape.JsonElement)
        };

        return new TypeUsage(
            typeName,
            typeShape,
            SchemaAllowsNull(schema),
            isOptional: !required);
    }

    public string? TryResolveSchemaReferenceName(IOpenApiSchema? schema)
    {
        if (TryResolveSchemaReferenceId(schema) is { } schemaReferenceId
            && catalog.TryGetComponentSchemaName(schemaReferenceId, out var schemaName))
        {
            return schemaName;
        }

        return null;
    }

    public bool TryGetDictionaryValueType(IOpenApiSchema schema, out string valueType)
    {
        valueType = string.Empty;
        if (cache.TryGetDictionaryValueType(schema, out var cachedValueType))
        {
            if (cachedValueType is null)
            {
                return false;
            }

            valueType = cachedValueType;
            return true;
        }

        var dictionaryValueTypes = new HashSet<string>(StringComparer.Ordinal);
        CollectDictionaryValueTypes(schema, dictionaryValueTypes, new HashSet<string>(StringComparer.Ordinal));
        if (dictionaryValueTypes.Count == 0)
        {
            cache.SetDictionaryValueType(schema, valueType: null);
            return false;
        }

        if (dictionaryValueTypes.Count > 1)
        {
            valueType = "JsonElement";
            cache.SetDictionaryValueType(schema, valueType);
            return true;
        }

        foreach (var dictionaryValueType in dictionaryValueTypes)
        {
            valueType = dictionaryValueType;
            break;
        }

        cache.SetDictionaryValueType(schema, valueType);
        return true;
    }

    public IOpenApiSchema ResolveSchemaReference(IOpenApiSchema schema)
    {
        if (schema is IOpenApiReferenceHolder<JsonSchemaReference> { Reference.Id: not null } referenceHolder
            && document.Components?.Schemas is { } schemas
            && schemas.TryGetValue(referenceHolder.Reference.Id, out var resolvedSchema))
        {
            return resolvedSchema;
        }

        return schema;
    }

    public TypeShape GetTypeShape(IOpenApiSchema schema)
    {
        var resolvedSchema = ResolveSchemaReference(schema);
        if (cache.TryGetTypeShape(resolvedSchema, out var cachedTypeShape))
        {
            return cachedTypeShape;
        }

        var typeShape = CreateTypeShape(resolvedSchema);
        cache.SetTypeShape(resolvedSchema, typeShape);
        return typeShape;
    }

    public bool SchemaAllowsNull(IOpenApiSchema schema)
    {
        if (cache.TryGetNullability(schema, out var cachedNullability))
        {
            return cachedNullability;
        }

        var allowsNull = CalculateSchemaAllowsNull(schema);
        cache.SetNullability(schema, allowsNull);
        return allowsNull;
    }

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

    public static string GetSchemaIdentity(IOpenApiSchema schema)
    {
        if (schema is IOpenApiReferenceHolder<JsonSchemaReference> { Reference.Id: not null } referenceHolder)
        {
            return $"ref:{referenceHolder.Reference.Id}";
        }

        return $"obj:{System.Runtime.CompilerServices.RuntimeHelpers.GetHashCode(schema).ToString(CultureInfo.InvariantCulture)}";
    }

    public static string? TryResolveSchemaReferenceId(IOpenApiSchema? schema)
    {
        return schema is IOpenApiReferenceHolder<JsonSchemaReference> { Reference.Id: not null } referenceHolder
            ? referenceHolder.Reference.Id
            : null;
    }

    public static bool IsNullOnlySchema(IOpenApiSchema? schema)
    {
        return schema is not null
            && schema.Type == JsonSchemaType.Null
            && string.IsNullOrWhiteSpace(schema.Format)
            && (schema.Properties?.Count ?? 0) == 0
            && (schema.AllOf?.Count ?? 0) == 0
            && (schema.AnyOf?.Count ?? 0) == 0
            && (schema.OneOf?.Count ?? 0) == 0;
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

    private bool TryResolveCompositeTypeUsage(IOpenApiSchema schema, bool required, out TypeUsage typeUsage)
    {
        if (TryResolveSchemaUnion(schema.OneOf, required, out typeUsage)
            || TryResolveSchemaUnion(schema.AnyOf, required, out typeUsage)
            || TryResolveSchemaUnion(schema.AllOf, required, out typeUsage))
        {
            return true;
        }

        typeUsage = null!;
        return false;
    }

    private bool TryResolveSchemaUnion(IList<IOpenApiSchema>? schemas, bool required, out TypeUsage typeUsage)
    {
        typeUsage = null!;
        if (schemas is null || schemas.Count == 0)
        {
            return false;
        }

        var nullable = false;
        TypeUsage? representativeUsage = null;
        var memberTypeNames = new HashSet<string>(StringComparer.Ordinal);
        foreach (var child in schemas)
        {
            if (IsNullOnlySchema(child))
            {
                nullable = true;
                continue;
            }

            var childUsage = ResolveTypeUsage(child, required: true);
            representativeUsage ??= childUsage;
            memberTypeNames.Add(childUsage.NonNullableCSharpTypeName);
        }

        if (memberTypeNames.Count != 1 || representativeUsage is null)
        {
            return false;
        }

        typeUsage = new TypeUsage(
            representativeUsage.NonNullableCSharpTypeName,
            representativeUsage.Shape,
            nullable || representativeUsage.SchemaAllowsNull,
            isOptional: !required);
        return true;
    }

    private void CollectDictionaryValueTypes(IOpenApiSchema schema, HashSet<string> valueTypes, HashSet<string> visited)
    {
        var identity = GetSchemaIdentity(schema);
        if (!visited.Add(identity))
        {
            return;
        }

        if (schema.AdditionalProperties is not null)
        {
            valueTypes.Add(CSharpUtilities.TrimNullableTypeName(ResolveTypeUsage(schema.AdditionalProperties, required: true).CSharpTypeName));
        }

        if (schema.PatternProperties is not null)
        {
            foreach (var patternProperty in schema.PatternProperties)
            {
                valueTypes.Add(CSharpUtilities.TrimNullableTypeName(ResolveTypeUsage(patternProperty.Value, required: true).CSharpTypeName));
            }
        }

        if (schema.AllOf is not null)
        {
            foreach (var child in schema.AllOf)
            {
                CollectDictionaryValueTypes(child, valueTypes, visited);
            }
        }

        visited.Remove(identity);
    }

    private TypeShape CreateTypeShape(IOpenApiSchema resolvedSchema)
    {
        if (IsEnumSchema(resolvedSchema))
        {
            return TypeShape.Enum;
        }

        if (TryGetDictionaryValueType(resolvedSchema, out _))
        {
            return TypeShape.Dictionary;
        }

        var baseType = resolvedSchema.Type & ~JsonSchemaType.Null;
        return baseType switch
        {
            JsonSchemaType.Integer or JsonSchemaType.Number or JsonSchemaType.Boolean => TypeShape.Primitive,
            JsonSchemaType.String when string.Equals(resolvedSchema.Format, "binary", StringComparison.OrdinalIgnoreCase) => TypeShape.Binary,
            JsonSchemaType.String when string.Equals(resolvedSchema.Format, "date", StringComparison.OrdinalIgnoreCase) => TypeShape.Primitive,
            JsonSchemaType.String when string.Equals(resolvedSchema.Format, "date-time", StringComparison.OrdinalIgnoreCase) => TypeShape.Primitive,
            JsonSchemaType.String when string.Equals(resolvedSchema.Format, "uuid", StringComparison.OrdinalIgnoreCase) => TypeShape.Primitive,
            JsonSchemaType.String => TypeShape.String,
            JsonSchemaType.Array => TypeShape.Array,
            JsonSchemaType.Object => TypeShape.Object,
            _ when resolvedSchema.AllOf is { Count: > 0 } || (resolvedSchema.Properties?.Count ?? 0) > 0 => TypeShape.Object,
            _ => TypeShape.JsonElement
        };
    }

    private bool CalculateSchemaAllowsNull(IOpenApiSchema schema)
    {
        if (HasSchemaType(schema, JsonSchemaType.Null) || SchemaCompositionsAllowNull(schema))
        {
            return true;
        }

        var resolvedSchema = ResolveSchemaReference(schema);
        if (ReferenceEquals(resolvedSchema, schema))
        {
            return false;
        }

        return HasSchemaType(resolvedSchema, JsonSchemaType.Null) || SchemaCompositionsAllowNull(resolvedSchema);
    }

    private static bool SchemaCompositionsAllowNull(IOpenApiSchema schema)
    {
        return (schema.OneOf?.Any(IsNullOnlySchema) == true)
            || (schema.AnyOf?.Any(IsNullOnlySchema) == true)
            || (schema.AllOf?.Any(IsNullOnlySchema) == true);
    }

    private static bool HasSchemaType(IOpenApiSchema? schema, JsonSchemaType type)
    {
        return schema?.Type is { } schemaType && (schemaType & type) == type;
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

        return jsonValue.GetValueKind() switch
        {
            JsonValueKind.String => SchemaEnumKind.String,
            JsonValueKind.Number => IsIntegerLiteral(node.ToString())
                ? SchemaEnumKind.Integer
                : IsNumberLiteral(node.ToString()) ? SchemaEnumKind.Number : SchemaEnumKind.None,
            _ => SchemaEnumKind.None
        };
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

            if (expectedKind == SchemaEnumKind.Number
                && !CanParseNumberLiteral(value, GetNumberEnumValueType(schema)))
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
