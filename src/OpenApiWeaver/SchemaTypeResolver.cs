using Microsoft.OpenApi;

namespace OpenApiWeaver;

internal sealed class SchemaTypeResolver(
    SchemaCatalog catalog,
    SchemaAnalysisCache cache,
    SchemaReferenceResolver schemaReferenceResolver,
    SchemaEnumResolver schemaEnumResolver)
{
    public TypeUsage ResolveTypeUsage(IOpenApiSchema? schema, bool required)
    {
        if (schema is null)
        {
            return TypeUsage.Create("string", TypeShape.String, schemaAllowsNull: false, isOptional: !required);
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
            return TypeUsage.Create(
                schemaName,
                GetTypeShape(schemaReferenceResolver.ResolveSchemaReference(schema)),
                SchemaAllowsNull(schema),
                isOptional: !required);
        }

        if (catalog.TryGetInlineSchema(SchemaReferenceResolver.GetSchemaIdentity(schema), out var inlineSchema))
        {
            return TypeUsage.Create(
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
            return TypeUsage.Create(
                dictionaryType,
                TypeShape.Dictionary,
                SchemaAllowsNull(schema),
                isOptional: !required);
        }

        var resolvedSchema = schemaReferenceResolver.ResolveSchemaReference(schema);
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

        return TypeUsage.Create(
            typeName,
            typeShape,
            SchemaAllowsNull(schema),
            isOptional: !required);
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

    public TypeShape GetTypeShape(IOpenApiSchema schema)
    {
        var resolvedSchema = schemaReferenceResolver.ResolveSchemaReference(schema);
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
            if (SchemaReferenceResolver.IsNullOnlySchema(child))
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

        typeUsage = TypeUsage.Create(
            representativeUsage.NonNullableCSharpTypeName,
            representativeUsage.Shape,
            nullable || representativeUsage.SchemaAllowsNull,
            isOptional: !required);
        return true;
    }

    private void CollectDictionaryValueTypes(IOpenApiSchema schema, HashSet<string> valueTypes, HashSet<string> visited)
    {
        var identity = SchemaReferenceResolver.GetSchemaIdentity(schema);
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
        if (schemaEnumResolver.IsEnumSchema(resolvedSchema))
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

        var resolvedSchema = schemaReferenceResolver.ResolveSchemaReference(schema);
        if (ReferenceEquals(resolvedSchema, schema))
        {
            return false;
        }

        return HasSchemaType(resolvedSchema, JsonSchemaType.Null) || SchemaCompositionsAllowNull(resolvedSchema);
    }

    private static bool SchemaCompositionsAllowNull(IOpenApiSchema schema)
    {
        return (schema.OneOf?.Any(SchemaReferenceResolver.IsNullOnlySchema) == true)
            || (schema.AnyOf?.Any(SchemaReferenceResolver.IsNullOnlySchema) == true)
            || (schema.AllOf?.Any(SchemaReferenceResolver.IsNullOnlySchema) == true);
    }

    private static bool HasSchemaType(IOpenApiSchema? schema, JsonSchemaType type)
    {
        return schema?.Type is { } schemaType && (schemaType & type) == type;
    }

}
