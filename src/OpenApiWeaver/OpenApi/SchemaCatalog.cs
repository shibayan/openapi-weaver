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

    public InlineSchemaInfo AddInlineSchema(string identity, string? parentTypeName, string suggestedTypeName, IOpenApiSchema schema, string? companionTypeSuffix = null)
    {
        var declaredTypeName = AllocateTypeName(parentTypeName, suggestedTypeName, companionTypeSuffix);
        var inlineSchema = new InlineSchemaInfo(
            BuildQualifiedTypeName(parentTypeName, declaredTypeName),
            declaredTypeName,
            parentTypeName,
            schema,
            companionTypeSuffix is null ? null : declaredTypeName + companionTypeSuffix);

        _inlineSchemasByIdentity.Add(identity, inlineSchema);
        _inlineSchemas.Add(inlineSchema);
        return inlineSchema;
    }

    // When companionTypeSuffix is set, the type is emitted with a sibling type (e.g. `StatusJsonConverter` next to
    // `Status`), so both names are allocated together.
    public string AllocateTypeName(string? parentTypeName, string suggestedTypeName, string? companionTypeSuffix = null)
    {
        var baseTypeName = string.IsNullOrWhiteSpace(suggestedTypeName) ? "Model" : suggestedTypeName;
        var enclosingTypeName = GetDeclaredTypeName(parentTypeName);
        var candidate = baseTypeName;
        var suffix = 2;

        // A nested type cannot share the name of its enclosing type (CS0542).
        bool IsAvailable(string name)
            => !string.Equals(name, enclosingTypeName, StringComparison.Ordinal)
                && !_usedTypeNames.Contains(BuildQualifiedTypeName(parentTypeName, name));

        while (!IsAvailable(candidate) || (companionTypeSuffix is not null && !IsAvailable(candidate + companionTypeSuffix)))
        {
            candidate = baseTypeName + suffix.ToString(CultureInfo.InvariantCulture);
            suffix++;
        }

        _usedTypeNames.Add(BuildQualifiedTypeName(parentTypeName, candidate));
        if (companionTypeSuffix is not null)
        {
            _usedTypeNames.Add(BuildQualifiedTypeName(parentTypeName, candidate + companionTypeSuffix));
        }

        return candidate;
    }

    private static string? GetDeclaredTypeName(string? parentTypeName)
    {
        if (string.IsNullOrWhiteSpace(parentTypeName))
        {
            return null;
        }

        var lastDotIndex = parentTypeName!.LastIndexOf('.');
        return lastDotIndex < 0 ? parentTypeName : parentTypeName.Substring(lastDotIndex + 1);
    }

    private static string BuildQualifiedTypeName(string? parentTypeName, string declaredTypeName)
        => string.IsNullOrWhiteSpace(parentTypeName) ? declaredTypeName : $"{parentTypeName}.{declaredTypeName}";
}

internal sealed class InlineSchemaInfo(string typeName, string declaredTypeName, string? parentTypeName, IOpenApiSchema schema, string? companionTypeName = null)
{
    public string TypeName { get; } = typeName;
    public string DeclaredTypeName { get; } = declaredTypeName;
    public string? ParentTypeName { get; } = parentTypeName;
    public IOpenApiSchema Schema { get; } = schema;

    // The sibling type emitted with this one (e.g. its JsonConverter), if any.
    public string? CompanionTypeName { get; } = companionTypeName;
}
