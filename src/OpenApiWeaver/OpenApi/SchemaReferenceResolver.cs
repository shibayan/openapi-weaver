using System.Globalization;

using Microsoft.OpenApi;

namespace OpenApiWeaver.OpenApi;

internal sealed class SchemaReferenceResolver(OpenApiDocument document, SchemaCatalog catalog)
{
    public string? TryResolveSchemaReferenceName(IOpenApiSchema? schema)
    {
        if (TryResolveSchemaReferenceId(schema) is { } schemaReferenceId
            && catalog.TryGetComponentSchemaName(schemaReferenceId, out var schemaName))
        {
            return schemaName;
        }

        return null;
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
}
