using Microsoft.OpenApi;

namespace OpenApiWeaver.OpenApi;

internal static class SchemaPropertyCollector
{
    public static List<SchemaPropertyInfo> Collect(
        IOpenApiSchema schema,
        ISet<string>? ignoredPropertyNames = null,
        ISet<string>? ignoredSchemaReferences = null)
    {
        var properties = new List<SchemaPropertyInfo>();
        var indices = new Dictionary<string, int>(StringComparer.Ordinal);
        var requiredNames = new HashSet<string>(StringComparer.Ordinal);
        CollectCore(schema, properties, indices, requiredNames, new HashSet<string>(StringComparer.Ordinal), ignoredPropertyNames, ignoredSchemaReferences);

        for (var i = 0; i < properties.Count; i++)
        {
            var property = properties[i];
            if (!property.Required && requiredNames.Contains(property.Name))
            {
                properties[i] = new SchemaPropertyInfo(property.Name, property.Schema, required: true, property.ReadOnly, property.WriteOnly);
            }
        }

        return properties;
    }

    private static void CollectCore(
        IOpenApiSchema schema,
        List<SchemaPropertyInfo> properties,
        Dictionary<string, int> indices,
        HashSet<string> requiredNames,
        HashSet<string> visited,
        ISet<string>? ignoredPropertyNames,
        ISet<string>? ignoredSchemaReferences)
    {
        var schemaReferenceId = SchemaReferenceResolver.TryResolveSchemaReferenceId(schema);
        if (schemaReferenceId is not null && ignoredSchemaReferences?.Contains(schemaReferenceId) == true)
        {
            return;
        }

        var identity = SchemaReferenceResolver.GetSchemaIdentity(schema);
        if (!visited.Add(identity))
        {
            return;
        }

        if (schema.Required is not null)
        {
            foreach (var requiredName in schema.Required)
            {
                requiredNames.Add(requiredName);
            }
        }

        if (schema.AllOf is not null)
        {
            foreach (var child in schema.AllOf)
            {
                CollectCore(child, properties, indices, requiredNames, visited, ignoredPropertyNames, ignoredSchemaReferences);
            }
        }

        if (schema.Properties is not null)
        {
            foreach (var property in schema.Properties)
            {
                if (ignoredPropertyNames?.Contains(property.Key) == true)
                {
                    continue;
                }

                var item = new SchemaPropertyInfo(
                    property.Key,
                    property.Value,
                    schema.Required?.Contains(property.Key) == true,
                    property.Value.ReadOnly,
                    property.Value.WriteOnly);

                if (indices.TryGetValue(property.Key, out var index))
                {
                    properties[index] = item;
                }
                else
                {
                    indices.Add(property.Key, properties.Count);
                    properties.Add(item);
                }
            }
        }

        visited.Remove(identity);
    }
}

internal sealed class SchemaPropertyInfo(string name, IOpenApiSchema schema, bool required, bool readOnly, bool writeOnly)
{
    public string Name { get; } = name;
    public IOpenApiSchema Schema { get; } = schema;
    public bool Required { get; } = required;
    public bool ReadOnly { get; } = readOnly;
    public bool WriteOnly { get; } = writeOnly;
}
