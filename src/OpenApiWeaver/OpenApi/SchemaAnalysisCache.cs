using Microsoft.OpenApi;

namespace OpenApiWeaver.OpenApi;

internal sealed class SchemaAnalysisCache
{
    private readonly Dictionary<IOpenApiSchema, TypeUsage> _requiredTypeUsagesBySchema = new(ReferenceEqualityComparer<IOpenApiSchema>.Instance);
    private readonly Dictionary<IOpenApiSchema, TypeUsage> _optionalTypeUsagesBySchema = new(ReferenceEqualityComparer<IOpenApiSchema>.Instance);
    private readonly Dictionary<IOpenApiSchema, string?> _dictionaryValueTypesBySchema = new(ReferenceEqualityComparer<IOpenApiSchema>.Instance);
    private readonly Dictionary<IOpenApiSchema, TypeShape> _typeShapesBySchema = new(ReferenceEqualityComparer<IOpenApiSchema>.Instance);
    private readonly Dictionary<IOpenApiSchema, bool> _schemaNullabilityBySchema = new(ReferenceEqualityComparer<IOpenApiSchema>.Instance);
    private readonly Dictionary<IOpenApiSchema, SchemaEnumKind> _schemaEnumKindsBySchema = new(ReferenceEqualityComparer<IOpenApiSchema>.Instance);

    public bool TryGetTypeUsage(IOpenApiSchema schema, bool required, out TypeUsage typeUsage)
    {
        var cache = required ? _requiredTypeUsagesBySchema : _optionalTypeUsagesBySchema;
        return cache.TryGetValue(schema, out typeUsage!);
    }

    public void SetTypeUsage(IOpenApiSchema schema, bool required, TypeUsage typeUsage)
    {
        var cache = required ? _requiredTypeUsagesBySchema : _optionalTypeUsagesBySchema;
        cache[schema] = typeUsage;
    }

    public bool TryGetDictionaryValueType(IOpenApiSchema schema, out string? valueType)
        => _dictionaryValueTypesBySchema.TryGetValue(schema, out valueType);

    public void SetDictionaryValueType(IOpenApiSchema schema, string? valueType)
    {
        _dictionaryValueTypesBySchema[schema] = valueType;
    }

    public bool TryGetTypeShape(IOpenApiSchema schema, out TypeShape typeShape)
        => _typeShapesBySchema.TryGetValue(schema, out typeShape);

    public void SetTypeShape(IOpenApiSchema schema, TypeShape typeShape)
    {
        _typeShapesBySchema[schema] = typeShape;
    }

    public bool TryGetNullability(IOpenApiSchema schema, out bool allowsNull)
        => _schemaNullabilityBySchema.TryGetValue(schema, out allowsNull);

    public void SetNullability(IOpenApiSchema schema, bool allowsNull)
    {
        _schemaNullabilityBySchema[schema] = allowsNull;
    }

    public bool TryGetEnumKind(IOpenApiSchema schema, out SchemaEnumKind kind)
        => _schemaEnumKindsBySchema.TryGetValue(schema, out kind);

    public void SetEnumKind(IOpenApiSchema schema, SchemaEnumKind kind)
    {
        _schemaEnumKindsBySchema[schema] = kind;
    }

    private sealed class ReferenceEqualityComparer<T> : IEqualityComparer<T>
        where T : class
    {
        public static ReferenceEqualityComparer<T> Instance { get; } = new();

        private ReferenceEqualityComparer()
        {
        }

        public bool Equals(T? x, T? y)
            => ReferenceEquals(x, y);

        public int GetHashCode(T obj)
            => System.Runtime.CompilerServices.RuntimeHelpers.GetHashCode(obj);
    }
}
