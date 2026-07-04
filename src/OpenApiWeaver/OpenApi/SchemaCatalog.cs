using System.Globalization;

using Microsoft.OpenApi;

namespace OpenApiWeaver.OpenApi;

internal sealed class SchemaCatalog
{
    private readonly Dictionary<string, string> _componentSchemaNames = new(StringComparer.Ordinal);
    private readonly Dictionary<string, InlineSchemaInfo> _inlineSchemasByIdentity = new(StringComparer.Ordinal);
    private readonly HashSet<string> _usedTypeNames = new(StringComparer.Ordinal);
    private readonly List<InlineSchemaInfo> _inlineSchemas = [];

    public IEnumerable<string> ComponentTypeNames => _componentSchemaNames.Values;

    public IReadOnlyList<InlineSchemaInfo> InlineSchemas => _inlineSchemas;

    public void ReserveTypeName(string typeName)
    {
        _usedTypeNames.Add(typeName);
    }

    public void AddComponentSchemaName(string schemaKey, string typeName)
    {
        _componentSchemaNames[schemaKey] = typeName;
    }

    public string GetComponentSchemaName(string schemaKey)
        => _componentSchemaNames[schemaKey];

    public bool TryGetComponentSchemaName(string schemaKey, out string typeName)
        => _componentSchemaNames.TryGetValue(schemaKey, out typeName!);

    public bool TryGetInlineSchema(string identity, out InlineSchemaInfo inlineSchema)
        => _inlineSchemasByIdentity.TryGetValue(identity, out inlineSchema!);

    public InlineSchemaInfo AddInlineSchema(string identity, string? parentTypeName, string suggestedTypeName, IOpenApiSchema schema)
    {
        var declaredTypeName = AllocateTypeName(parentTypeName, suggestedTypeName);
        var inlineSchema = new InlineSchemaInfo(
            BuildQualifiedTypeName(parentTypeName, declaredTypeName),
            declaredTypeName,
            parentTypeName,
            schema);

        _inlineSchemasByIdentity.Add(identity, inlineSchema);
        _inlineSchemas.Add(inlineSchema);
        return inlineSchema;
    }

    public string AllocateTypeName(string? parentTypeName, string suggestedTypeName)
    {
        var baseTypeName = string.IsNullOrWhiteSpace(suggestedTypeName) ? "Model" : suggestedTypeName;
        var candidate = baseTypeName;
        var suffix = 2;

        while (!_usedTypeNames.Add(BuildQualifiedTypeName(parentTypeName, candidate)))
        {
            candidate = baseTypeName + suffix.ToString(CultureInfo.InvariantCulture);
            suffix++;
        }

        return candidate;
    }

    private static string BuildQualifiedTypeName(string? parentTypeName, string declaredTypeName)
        => string.IsNullOrWhiteSpace(parentTypeName) ? declaredTypeName : $"{parentTypeName}.{declaredTypeName}";
}

internal sealed class InlineSchemaInfo(string typeName, string declaredTypeName, string? parentTypeName, IOpenApiSchema schema)
{
    public string TypeName { get; } = typeName;
    public string DeclaredTypeName { get; } = declaredTypeName;
    public string? ParentTypeName { get; } = parentTypeName;
    public IOpenApiSchema Schema { get; } = schema;
}
