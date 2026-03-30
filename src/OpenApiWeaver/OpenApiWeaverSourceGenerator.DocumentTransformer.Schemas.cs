using System.Globalization;

using Microsoft.OpenApi;

namespace OpenApiWeaver;

public sealed partial class OpenApiWeaverSourceGenerator
{
    private sealed partial class DocumentTransformer
    {
        private void RegisterSchemaNames()
        {
            if (_document.Components?.Schemas is null)
            {
                return;
            }

            foreach (var schema in _document.Components.Schemas)
            {
                var schemaName = SafeIdentifier(ToPascalCase(schema.Key));
                _schemaNames[schema.Key] = schemaName;
                _usedSchemaTypeNames.Add(schemaName);
            }
        }

        private void RegisterInlineSchemaNames()
        {
            if (_document.Components?.Schemas is null)
            {
                return;
            }

            foreach (var schema in _document.Components.Schemas)
            {
                RegisterNestedInlineSchemas(_schemaNames[schema.Key], schema.Value, new HashSet<string>(StringComparer.Ordinal));
            }
        }

        private List<SchemaDefinition> BuildSchemaDefinitions()
        {
            var schemas = new List<SchemaDefinition>();

            if (_document.Components?.Schemas is not null)
            {
                foreach (var schema in _document.Components.Schemas)
                {
                    schemas.Add(CreateSchemaDefinition(_schemaNames[schema.Key], schema.Value));
                }
            }

            foreach (var inlineSchema in _inlineSchemas)
            {
                schemas.Add(CreateSchemaDefinition(inlineSchema.TypeName, inlineSchema.Schema));
            }

            return schemas;
        }

        private SchemaDefinition CreateSchemaDefinition(string typeName, IOpenApiSchema schema)
        {
            var properties = new List<SchemaPropertyDefinition>();
            foreach (var property in GetSchemaProperties(schema))
            {
                var propertyName = SafeIdentifier(ToPascalCase(property.Name));
                properties.Add(new SchemaPropertyDefinition(
                    property.Name,
                    propertyName,
                    ResolveTypeName(property.Schema, property.Required),
                    property.Required,
                    property.Schema.Title ?? propertyName,
                    property.Schema.Description));
            }

            var enumValues = new List<string>();
            if (IsEnumSchema(schema))
            {
                foreach (var item in schema.Enum ?? [])
                {
                    var enumValue = item?.ToString();
                    if (!string.IsNullOrWhiteSpace(enumValue))
                    {
                        enumValues.Add(enumValue!);
                    }
                }
            }

            return new SchemaDefinition(
                typeName,
                schema.Title ?? typeName,
                schema.Description,
                TryGetDictionaryValueType(schema, out var dictionaryValueType) ? dictionaryValueType : null,
                properties,
                enumValues);
        }

        private void RegisterNestedInlineSchemas(string ownerTypeName, IOpenApiSchema? schema, HashSet<string> visited)
        {
            if (schema is null || TryResolveSchemaReferenceName(schema) is not null)
            {
                return;
            }

            var identity = GetSchemaIdentity(schema);
            if (!visited.Add(identity))
            {
                return;
            }

            if (schema.Properties is not null)
            {
                foreach (var property in schema.Properties)
                {
                    RegisterInlineSchemaChild(ownerTypeName, property.Key, property.Value, visited);
                }
            }

            if (schema.Items is not null)
            {
                RegisterInlineSchemaChild(ownerTypeName, "item", schema.Items, visited);
            }

            if (schema.AdditionalProperties is not null)
            {
                RegisterInlineSchemaChild(ownerTypeName, "value", schema.AdditionalProperties, visited);
            }

            if (schema.PatternProperties is not null)
            {
                foreach (var patternProperty in schema.PatternProperties)
                {
                    RegisterInlineSchemaChild(ownerTypeName, patternProperty.Key, patternProperty.Value, visited);
                }
            }

            if (schema.AllOf is not null)
            {
                foreach (var child in schema.AllOf)
                {
                    RegisterNestedInlineSchemas(ownerTypeName, child, visited);
                }
            }

            if (schema.OneOf is not null)
            {
                foreach (var child in schema.OneOf)
                {
                    RegisterNestedInlineSchemas(ownerTypeName, child, visited);
                }
            }

            if (schema.AnyOf is not null)
            {
                foreach (var child in schema.AnyOf)
                {
                    RegisterNestedInlineSchemas(ownerTypeName, child, visited);
                }
            }

            visited.Remove(identity);
        }

        private void RegisterInlineSchemaChild(string ownerTypeName, string childName, IOpenApiSchema? childSchema, HashSet<string> visited)
        {
            if (childSchema is null)
            {
                return;
            }

            var suggestedTypeName = BuildInlineSchemaTypeName(ownerTypeName, childName);
            if (TryRegisterInlineSchema(childSchema, suggestedTypeName, out var inlineTypeName))
            {
                RegisterNestedInlineSchemas(inlineTypeName, childSchema, visited);
                return;
            }

            RegisterNestedInlineSchemas(suggestedTypeName, childSchema, visited);
        }

        private bool TryRegisterInlineSchema(IOpenApiSchema schema, string suggestedTypeName, out string inlineTypeName)
        {
            inlineTypeName = string.Empty;
            if (!CanGenerateInlineSchema(schema))
            {
                return false;
            }

            var identity = GetSchemaIdentity(schema);
            if (_inlineSchemaNames.TryGetValue(identity, out inlineTypeName))
            {
                return true;
            }

            inlineTypeName = AllocateSchemaTypeName(suggestedTypeName);
            _inlineSchemaNames.Add(identity, inlineTypeName);
            _inlineSchemas.Add(new InlineSchemaInfo(inlineTypeName, schema));
            return true;
        }

        private bool CanGenerateInlineSchema(IOpenApiSchema schema)
        {
            if (TryResolveSchemaReferenceName(schema) is not null
                || IsEnumSchema(schema)
                || TryGetDictionaryValueType(schema, out _)
                || schema.OneOf is { Count: > 0 }
                || schema.AnyOf is { Count: > 0 })
            {
                return false;
            }

            var baseType = schema.Type & ~JsonSchemaType.Null;
            return baseType == JsonSchemaType.Object || schema.AllOf is { Count: > 0 } || (schema.Properties?.Count ?? 0) > 0;
        }

        private string AllocateSchemaTypeName(string suggestedTypeName)
        {
            var baseTypeName = SafeIdentifier(suggestedTypeName);
            if (string.IsNullOrWhiteSpace(baseTypeName))
            {
                baseTypeName = "InlineObject";
            }

            var candidate = baseTypeName;
            var suffix = 2;
            while (!_usedSchemaTypeNames.Add(candidate))
            {
                candidate = baseTypeName + suffix.ToString(CultureInfo.InvariantCulture);
                suffix++;
            }

            return candidate;
        }

        private static string BuildInlineSchemaTypeName(string ownerTypeName, string childName)
        {
            return SafeIdentifier(ownerTypeName + ToPascalCase(childName));
        }

        private string ResolveTypeName(IOpenApiSchema? schema, bool required)
        {
            if (schema is null)
            {
                return required ? "string" : "string?";
            }

            if (schema is IOpenApiReferenceHolder<JsonSchemaReference> { Reference.Id: not null } referenceHolder
                && _schemaNames.TryGetValue(referenceHolder.Reference.Id, out var schemaName))
            {
                return required && !IsNullableSchema(schema) ? schemaName : $"{schemaName}?";
            }

            if (_inlineSchemaNames.TryGetValue(GetSchemaIdentity(schema), out var inlineSchemaName))
            {
                return required && !IsNullableSchema(schema) ? inlineSchemaName : $"{inlineSchemaName}?";
            }

            if (TryResolveCompositeTypeName(schema, required, out var compositeTypeName))
            {
                return compositeTypeName;
            }

            if (TryGetDictionaryValueType(schema, out var dictionaryValueType))
            {
                var dictionaryType = $"IReadOnlyDictionary<string, {dictionaryValueType}>";
                return required && !IsNullableSchema(schema) ? dictionaryType : $"{dictionaryType}?";
            }

            var baseType = schema.Type & ~JsonSchemaType.Null;
            var typeName = baseType switch
            {
                JsonSchemaType.Integer when string.Equals(schema.Format, "int64", StringComparison.OrdinalIgnoreCase) => "long",
                JsonSchemaType.Integer => "int",
                JsonSchemaType.Number when string.Equals(schema.Format, "float", StringComparison.OrdinalIgnoreCase) => "float",
                JsonSchemaType.Number => "double",
                JsonSchemaType.Boolean => "bool",
                JsonSchemaType.String when string.Equals(schema.Format, "date", StringComparison.OrdinalIgnoreCase) => "DateOnly",
                JsonSchemaType.String when string.Equals(schema.Format, "date-time", StringComparison.OrdinalIgnoreCase) => "DateTimeOffset",
                JsonSchemaType.String when string.Equals(schema.Format, "uuid", StringComparison.OrdinalIgnoreCase) => "Guid",
                JsonSchemaType.String when string.Equals(schema.Format, "binary", StringComparison.OrdinalIgnoreCase) => "byte[]",
                JsonSchemaType.Array => $"IReadOnlyList<{ResolveTypeName(schema.Items, required: true)}>",
                JsonSchemaType.String => "string",
                _ => "JsonElement"
            };

            return required && !IsNullableSchema(schema) ? typeName : $"{typeName}?";
        }

        private List<SchemaPropertyInfo> GetSchemaProperties(IOpenApiSchema schema)
        {
            var properties = new List<SchemaPropertyInfo>();
            var indices = new Dictionary<string, int>(StringComparer.Ordinal);
            CollectSchemaProperties(schema, properties, indices, new HashSet<string>(StringComparer.Ordinal));
            return properties;
        }

        private void CollectSchemaProperties(
            IOpenApiSchema schema,
            List<SchemaPropertyInfo> properties,
            Dictionary<string, int> indices,
            HashSet<string> visited)
        {
            var identity = GetSchemaIdentity(schema);
            if (!visited.Add(identity))
            {
                return;
            }

            if (schema.AllOf is not null)
            {
                foreach (var child in schema.AllOf)
                {
                    CollectSchemaProperties(child, properties, indices, visited);
                }
            }

            if (schema.Properties is not null)
            {
                foreach (var property in schema.Properties)
                {
                    var item = new SchemaPropertyInfo(
                        property.Key,
                        property.Value,
                        schema.Required?.Contains(property.Key) == true);

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

        private static string GetSchemaIdentity(IOpenApiSchema schema)
        {
            if (schema is IOpenApiReferenceHolder<JsonSchemaReference> { Reference.Id: not null } referenceHolder)
            {
                return $"ref:{referenceHolder.Reference.Id}";
            }

            return $"obj:{System.Runtime.CompilerServices.RuntimeHelpers.GetHashCode(schema).ToString(CultureInfo.InvariantCulture)}";
        }

        private string? TryResolveSchemaReferenceName(IOpenApiSchema? schema)
        {
            if (schema is IOpenApiReferenceHolder<JsonSchemaReference> { Reference.Id: not null } referenceHolder
                && _schemaNames.TryGetValue(referenceHolder.Reference.Id, out var schemaName))
            {
                return schemaName;
            }

            return null;
        }

        private bool TryResolveCompositeTypeName(IOpenApiSchema schema, bool required, out string typeName)
        {
            if (TryResolveSchemaUnion(schema.OneOf, required, out typeName)
                || TryResolveSchemaUnion(schema.AnyOf, required, out typeName)
                || TryResolveSchemaUnion(schema.AllOf, required, out typeName))
            {
                return true;
            }

            typeName = string.Empty;
            return false;
        }

        private bool TryResolveSchemaUnion(IList<IOpenApiSchema>? schemas, bool required, out string typeName)
        {
            typeName = string.Empty;
            if (schemas is null || schemas.Count == 0)
            {
                return false;
            }

            var nullable = false;
            var memberTypeNames = new HashSet<string>(StringComparer.Ordinal);
            foreach (var child in schemas)
            {
                if (IsNullOnlySchema(child))
                {
                    nullable = true;
                    continue;
                }

                var childTypeName = TryResolveSchemaReferenceName(child) ?? ResolveTypeName(child, required: true);
                memberTypeNames.Add(TrimNullableTypeName(childTypeName));
            }

            if (memberTypeNames.Count != 1)
            {
                return false;
            }

            var resolvedTypeName = memberTypeNames.Single();
            typeName = nullable || !required ? MakeNullableTypeName(resolvedTypeName) : resolvedTypeName;
            return true;
        }

        private bool TryGetDictionaryValueType(IOpenApiSchema schema, out string valueType)
        {
            valueType = string.Empty;
            var dictionaryValueTypes = new HashSet<string>(StringComparer.Ordinal);
            CollectDictionaryValueTypes(schema, dictionaryValueTypes, new HashSet<string>(StringComparer.Ordinal));
            if (dictionaryValueTypes.Count == 0)
            {
                return false;
            }

            if (dictionaryValueTypes.Count > 1)
            {
                valueType = "JsonElement";
                return true;
            }

            valueType = dictionaryValueTypes.Single();
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
                valueTypes.Add(TrimNullableTypeName(ResolveTypeName(schema.AdditionalProperties, required: true)));
            }

            if (schema.PatternProperties is not null)
            {
                foreach (var patternProperty in schema.PatternProperties)
                {
                    valueTypes.Add(TrimNullableTypeName(ResolveTypeName(patternProperty.Value, required: true)));
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

        private IOpenApiSchema ResolveSchemaReference(IOpenApiSchema schema)
        {
            if (schema is IOpenApiReferenceHolder<JsonSchemaReference> { Reference.Id: not null } referenceHolder
                && _document.Components?.Schemas is { } schemas
                && schemas.TryGetValue(referenceHolder.Reference.Id, out var resolvedSchema))
            {
                return resolvedSchema;
            }

            return schema;
        }

        private static bool HasSchemaType(IOpenApiSchema? schema, JsonSchemaType type)
        {
            return schema?.Type is { } schemaType && (schemaType & type) == type;
        }

        private static bool IsNullableSchema(IOpenApiSchema schema)
        {
            return HasSchemaType(schema, JsonSchemaType.Null);
        }

        private static bool IsNullOnlySchema(IOpenApiSchema? schema)
        {
            return schema is not null
                && schema.Type == JsonSchemaType.Null
                && string.IsNullOrWhiteSpace(schema.Format)
                && (schema.Properties?.Count ?? 0) == 0
                && (schema.AllOf?.Count ?? 0) == 0
                && (schema.AnyOf?.Count ?? 0) == 0
                && (schema.OneOf?.Count ?? 0) == 0;
        }

        private static bool IsEnumSchema(IOpenApiSchema schema)
        {
            return schema.Enum is { Count: > 0 } && HasSchemaType(schema, JsonSchemaType.String);
        }

        private sealed class InlineSchemaInfo(string typeName, IOpenApiSchema schema)
        {
            public string TypeName { get; } = typeName;
            public IOpenApiSchema Schema { get; } = schema;
        }

        private sealed class SchemaPropertyInfo(string name, IOpenApiSchema schema, bool required)
        {
            public string Name { get; } = name;
            public IOpenApiSchema Schema { get; } = schema;
            public bool Required { get; } = required;
        }
    }
}
