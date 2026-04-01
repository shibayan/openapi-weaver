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
                var schemaName = _schemaNames[schema.Key];
                RegisterNestedInlineSchemas(schemaName, schema.Value, new HashSet<string>(StringComparer.Ordinal), schemaName, string.Empty);
            }
        }

        private List<SchemaDefinition> BuildSchemaDefinitions()
        {
            var schemas = new List<SchemaDefinition>();

            if (_document.Components?.Schemas is not null)
            {
                foreach (var schema in _document.Components.Schemas)
                {
                    var schemaName = _schemaNames[schema.Key];
                    schemas.Add(CreateSchemaDefinition(schemaName, schemaName, parentTypeName: null, schema.Value));
                }
            }

            foreach (var inlineSchema in _inlineSchemas)
            {
                schemas.Add(CreateSchemaDefinition(inlineSchema.TypeName, inlineSchema.DeclaredTypeName, inlineSchema.ParentTypeName, inlineSchema.Schema));
            }

            return schemas;
        }

        private SchemaDefinition CreateSchemaDefinition(string typeName, string declaredTypeName, string? parentTypeName, IOpenApiSchema schema)
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

            var enumKind = GetSchemaEnumKind(schema);
            var enumUnderlyingType = enumKind == SchemaEnumKind.Integer ? GetIntegerEnumUnderlyingType(schema) : null;
            var enumMembers = enumKind == SchemaEnumKind.None ? [] : CreateEnumMembers(schema, enumKind);

            return new SchemaDefinition(
                typeName,
                declaredTypeName,
                parentTypeName,
                schema.Title ?? typeName,
                schema.Description,
                TryGetDictionaryValueType(schema, out var dictionaryValueType) ? dictionaryValueType : null,
                properties,
                enumKind,
                enumUnderlyingType,
                enumMembers);
        }

        private void RegisterNestedInlineSchemas(string ownerTypeName, IOpenApiSchema? schema, HashSet<string> visited, string containingTypeName, string nestedNamePrefix)
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
                    RegisterInlineSchemaChild(ownerTypeName, containingTypeName, nestedNamePrefix, property.Key, property.Value, visited);
                }
            }

            if (schema.Items is not null)
            {
                RegisterInlineSchemaChild(ownerTypeName, containingTypeName, nestedNamePrefix, "item", schema.Items, visited);
            }

            if (schema.AdditionalProperties is not null)
            {
                RegisterInlineSchemaChild(ownerTypeName, containingTypeName, nestedNamePrefix, "value", schema.AdditionalProperties, visited);
            }

            if (schema.PatternProperties is not null)
            {
                foreach (var patternProperty in schema.PatternProperties)
                {
                    RegisterInlineSchemaChild(ownerTypeName, containingTypeName, nestedNamePrefix, patternProperty.Key, patternProperty.Value, visited);
                }
            }

            if (schema.AllOf is not null)
            {
                foreach (var child in schema.AllOf)
                {
                    RegisterNestedInlineSchemas(ownerTypeName, child, visited, containingTypeName, nestedNamePrefix);
                }
            }

            if (schema.OneOf is not null)
            {
                foreach (var child in schema.OneOf)
                {
                    RegisterNestedInlineSchemas(ownerTypeName, child, visited, containingTypeName, nestedNamePrefix);
                }
            }

            if (schema.AnyOf is not null)
            {
                foreach (var child in schema.AnyOf)
                {
                    RegisterNestedInlineSchemas(ownerTypeName, child, visited, containingTypeName, nestedNamePrefix);
                }
            }

            visited.Remove(identity);
        }

        private void RegisterInlineSchemaChild(string ownerTypeName, string containingTypeName, string nestedNamePrefix, string childName, IOpenApiSchema? childSchema, HashSet<string> visited)
        {
            if (childSchema is null)
            {
                return;
            }

            var suggestedTypeName = BuildInlineSchemaTypeName(nestedNamePrefix, childName, childSchema);
            if (TryRegisterInlineSchema(childSchema, containingTypeName, suggestedTypeName, out var inlineSchema))
            {
                RegisterNestedInlineSchemas(ownerTypeName, childSchema, visited, inlineSchema.TypeName, string.Empty);
                return;
            }

            var nextPrefix = CombineNestedTypeNamePrefix(nestedNamePrefix, childName);
            RegisterNestedInlineSchemas(ownerTypeName, childSchema, visited, containingTypeName, nextPrefix);
        }

        private bool TryRegisterInlineSchema(IOpenApiSchema schema, string parentTypeName, string suggestedTypeName, out InlineSchemaInfo inlineSchema)
        {
            inlineSchema = null!;
            if (!CanGenerateInlineSchema(schema))
            {
                return false;
            }

            var identity = GetSchemaIdentity(schema);
            if (_inlineSchemaNames.TryGetValue(identity, out var inlineTypeName))
            {
                foreach (var existingInlineSchema in _inlineSchemas)
                {
                    if (existingInlineSchema.TypeName == inlineTypeName)
                    {
                        inlineSchema = existingInlineSchema;
                        return true;
                    }
                }

                throw new InvalidOperationException($"Inline schema '{inlineTypeName}' was not registered.");
            }

            inlineSchema = AllocateInlineSchema(parentTypeName, suggestedTypeName, schema);
            _inlineSchemaNames.Add(identity, inlineSchema.TypeName);
            _inlineSchemas.Add(inlineSchema);
            return true;
        }

        private bool CanGenerateInlineSchema(IOpenApiSchema schema)
        {
            if (TryResolveSchemaReferenceName(schema) is not null
                || TryGetDictionaryValueType(schema, out _)
                || schema.OneOf is { Count: > 0 }
                || schema.AnyOf is { Count: > 0 })
            {
                return false;
            }

            var baseType = schema.Type & ~JsonSchemaType.Null;
            return IsEnumSchema(schema)
                || baseType == JsonSchemaType.Object
                || schema.AllOf is { Count: > 0 }
                || (schema.Properties?.Count ?? 0) > 0;
        }

        private InlineSchemaInfo AllocateInlineSchema(string parentTypeName, string suggestedTypeName, IOpenApiSchema schema)
        {
            var baseTypeName = SafeIdentifier(suggestedTypeName);
            if (string.IsNullOrWhiteSpace(baseTypeName))
            {
                baseTypeName = "InlineObject";
            }

            var candidate = baseTypeName;
            var suffix = 2;
            while (!_usedSchemaTypeNames.Add(BuildQualifiedTypeName(parentTypeName, candidate)))
            {
                candidate = baseTypeName + suffix.ToString(CultureInfo.InvariantCulture);
                suffix++;
            }

            var typeName = BuildQualifiedTypeName(parentTypeName, candidate);
            return new InlineSchemaInfo(typeName, candidate, parentTypeName, schema);
        }

        private static string BuildInlineSchemaTypeName(string nestedNamePrefix, string childName, IOpenApiSchema schema)
        {
            var suffix = childName switch
            {
                "item" => "Item",
                "value" => "Value",
                _ when GetSchemaEnumKind(schema) != SchemaEnumKind.None => ToPascalCase(childName) + "Enum",
                _ => ToPascalCase(childName) + "Model"
            };

            return SafeIdentifier(nestedNamePrefix + suffix);
        }

        private static string CombineNestedTypeNamePrefix(string nestedNamePrefix, string childName)
        {
            var segment = childName switch
            {
                "item" => "Item",
                "value" => "Value",
                _ => ToPascalCase(childName)
            };

            return SafeIdentifier(nestedNamePrefix + segment);
        }

        private static string BuildQualifiedTypeName(string parentTypeName, string declaredTypeName)
            => $"{parentTypeName}.{declaredTypeName}";

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
                JsonSchemaType.Number when string.Equals(schema.Format, "double", StringComparison.OrdinalIgnoreCase) => "double",
                JsonSchemaType.Number when string.Equals(schema.Format, "decimal", StringComparison.OrdinalIgnoreCase) => "decimal",
                JsonSchemaType.Number => "decimal",
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
            return GetSchemaEnumKind(schema) != SchemaEnumKind.None;
        }

        private static SchemaEnumKind GetSchemaEnumKind(IOpenApiSchema schema)
        {
            if (schema.Enum is not { Count: > 0 })
            {
                return SchemaEnumKind.None;
            }

            var baseType = schema.Type & ~JsonSchemaType.Null;
            return baseType switch
            {
                JsonSchemaType.String => SchemaEnumKind.String,
                JsonSchemaType.Integer => SchemaEnumKind.Integer,
                _ => SchemaEnumKind.None
            };
        }

        private static string GetIntegerEnumUnderlyingType(IOpenApiSchema schema)
        {
            return string.Equals(schema.Format, "int64", StringComparison.OrdinalIgnoreCase) ? "long" : "int";
        }

        private static List<SchemaEnumMemberDefinition> CreateEnumMembers(IOpenApiSchema schema, SchemaEnumKind enumKind)
        {
            var members = new List<SchemaEnumMemberDefinition>();
            var usedMemberNames = new Dictionary<string, int>(StringComparer.Ordinal);

            foreach (var item in schema.Enum ?? [])
            {
                var enumValue = item?.ToString();
                if (enumValue is null || string.IsNullOrWhiteSpace(enumValue))
                {
                    continue;
                }

                var memberName = enumKind == SchemaEnumKind.String
                    ? SafeIdentifier(ToPascalCase(enumValue))
                    : BuildIntegerEnumMemberName(enumValue);

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

        private static string BuildIntegerEnumMemberName(string value)
        {
            var digits = value.Trim();
            if (digits.StartsWith("-", StringComparison.Ordinal))
            {
                digits = "Minus" + digits.Substring(1);
            }

            return SafeIdentifier($"Value{digits}");
        }

        private sealed class InlineSchemaInfo(string typeName, string declaredTypeName, string parentTypeName, IOpenApiSchema schema)
        {
            public string TypeName { get; } = typeName;
            public string DeclaredTypeName { get; } = declaredTypeName;
            public string ParentTypeName { get; } = parentTypeName;
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
