using System.Globalization;

using Microsoft.OpenApi;

namespace OpenApiWeaver;

public sealed partial class ClientGenerator
{
    private sealed partial class Transformer
    {
        private void RegisterSchemaNames()
        {
            _usedSchemaTypeNames.Add(_clientName);
            _usedSchemaTypeNames.Add("OpenApiClientHelpers");
            _usedSchemaTypeNames.Add("OpenApiException");

            if (_document.Components?.Schemas is null)
            {
                return;
            }

            foreach (var schema in _document.Components.Schemas)
            {
                var schemaName = AllocateTypeName(parentTypeName: null, NormalizePascalIdentifier(schema.Key, "Model"));
                _schemaNames[schema.Key] = schemaName;
            }
        }

        private void RegisterPolymorphicSchemaInfo()
        {
            if (_document.Components?.Schemas is null)
            {
                return;
            }

            foreach (var schema in _document.Components.Schemas)
            {
                if (schema.Value.Discriminator is null)
                {
                    continue;
                }

                RegisterPolymorphicSchemaInfo(schema.Key, schema.Value);
            }
        }

        private void RegisterPolymorphicSchemaInfo(string schemaName, IOpenApiSchema schema)
        {
            if (schema.AnyOf is { Count: > 0 })
            {
                throw new UnsupportedGenerationException($"Schema '{schemaName}' uses discriminator with anyOf, which is not supported for compile-time code generation.");
            }

            if (schema.OneOf is not { Count: > 0 })
            {
                throw new UnsupportedGenerationException($"Schema '{schemaName}' uses discriminator without oneOf, which is not supported for compile-time code generation.");
            }

            var discriminatorPropertyName = schema.Discriminator?.PropertyName;
            if (string.IsNullOrWhiteSpace(discriminatorPropertyName))
            {
                throw new UnsupportedGenerationException($"Schema '{schemaName}' uses discriminator without a propertyName, which is not supported for compile-time code generation.");
            }

            var baseTypeName = _schemaNames[schemaName];
            var derivedSchemaNames = new HashSet<string>(StringComparer.Ordinal);
            var derivedTypes = new List<SchemaDerivedTypeDefinition>();
            var pendingDerivedSchemas = new List<(string TypeName, PolymorphicDerivedSchemaInfo Info)>();
            var usedDiscriminatorValues = new HashSet<string>(StringComparer.Ordinal);
            foreach (var child in schema.OneOf)
            {
                var derivedSchemaName = TryResolveSchemaReferenceId(child);
                if (derivedSchemaName is null)
                {
                    throw new UnsupportedGenerationException($"Schema '{schemaName}' uses discriminator with inline oneOf members, which is not supported for compile-time code generation.");
                }

                derivedSchemaNames.Add(derivedSchemaName);

                if (!_schemaNames.TryGetValue(derivedSchemaName, out var derivedTypeName))
                {
                    throw new UnsupportedGenerationException($"Schema '{schemaName}' discriminator references unknown schema '{derivedSchemaName}'.");
                }

                var discriminatorValue = ResolveDiscriminatorValue(schema.Discriminator!, derivedSchemaName);
                if (!usedDiscriminatorValues.Add(discriminatorValue))
                {
                    throw new UnsupportedGenerationException($"Schema '{schemaName}' uses duplicate discriminator value '{discriminatorValue}', which is not supported for compile-time code generation.");
                }

                if (_polymorphicDerivedSchemasByTypeName.TryGetValue(derivedTypeName, out var existingDerivedSchema)
                    && !string.Equals(existingDerivedSchema.BaseTypeName, baseTypeName, StringComparison.Ordinal))
                {
                    throw new UnsupportedGenerationException($"Schema '{derivedSchemaName}' is used by multiple discriminator hierarchies, which is not supported for compile-time code generation.");
                }

                pendingDerivedSchemas.Add((derivedTypeName, new PolymorphicDerivedSchemaInfo(schemaName, baseTypeName, discriminatorPropertyName!)));
                derivedTypes.Add(new SchemaDerivedTypeDefinition(derivedTypeName, discriminatorValue));
            }

            ValidateDiscriminatorMappings(schemaName, schema.Discriminator!, derivedSchemaNames);

            foreach (var (derivedTypeName, derivedInfo) in pendingDerivedSchemas)
            {
                _polymorphicDerivedSchemasByTypeName[derivedTypeName] = derivedInfo;
            }

            _polymorphicSchemasByTypeName[baseTypeName] = new PolymorphicSchemaInfo(discriminatorPropertyName!, derivedTypes);
        }

        private static string ResolveDiscriminatorValue(OpenApiDiscriminator discriminator, string derivedSchemaName)
        {
            if (discriminator.Mapping is not null)
            {
                foreach (var mapping in discriminator.Mapping)
                {
                    if (string.Equals(TryResolveSchemaReferenceId(mapping.Value), derivedSchemaName, StringComparison.Ordinal))
                    {
                        return mapping.Key;
                    }
                }
            }

            return derivedSchemaName;
        }

        private static void ValidateDiscriminatorMappings(string schemaName, OpenApiDiscriminator discriminator, HashSet<string> derivedSchemaNames)
        {
            if (discriminator.Mapping is null)
            {
                return;
            }

            foreach (var mapping in discriminator.Mapping)
            {
                var mappedSchemaName = TryResolveSchemaReferenceId(mapping.Value);
                if (mappedSchemaName is null || !derivedSchemaNames.Contains(mappedSchemaName))
                {
                    throw new UnsupportedGenerationException($"Schema '{schemaName}' discriminator mapping '{mapping.Key}' must reference a schema listed in oneOf.");
                }
            }
        }

        private void RegisterInlineSchemaNames()
        {
            if (_document.Components?.Schemas is not null)
            {
                foreach (var schema in _document.Components.Schemas)
                {
                    var schemaName = _schemaNames[schema.Key];
                    RegisterNestedInlineSchemas(schema.Value, new HashSet<string>(StringComparer.Ordinal), schemaName, string.Empty);
                }
            }

            RegisterOperationInlineSchemaNames();
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
            var ignoredPropertyNames = new HashSet<string>(StringComparer.Ordinal);
            var ignoredSchemaReferences = new HashSet<string>(StringComparer.Ordinal);
            string? baseTypeName = null;
            string? discriminatorPropertyName = null;
            IReadOnlyList<SchemaDerivedTypeDefinition> derivedTypes = [];

            if (_polymorphicSchemasByTypeName.TryGetValue(typeName, out var polymorphicSchema))
            {
                discriminatorPropertyName = polymorphicSchema.DiscriminatorPropertyName;
                ignoredPropertyNames.Add(polymorphicSchema.DiscriminatorPropertyName);
                derivedTypes = polymorphicSchema.DerivedTypes;
            }

            if (_polymorphicDerivedSchemasByTypeName.TryGetValue(typeName, out var polymorphicDerivedSchema))
            {
                baseTypeName = polymorphicDerivedSchema.BaseTypeName;
                ignoredPropertyNames.Add(polymorphicDerivedSchema.DiscriminatorPropertyName);
                ignoredSchemaReferences.Add(polymorphicDerivedSchema.BaseSchemaName);
            }

            var properties = new List<SchemaPropertyDefinition>();
            var usedPropertyNames = new HashSet<string>(StringComparer.Ordinal);
            foreach (var property in GetSchemaProperties(schema, ignoredPropertyNames, ignoredSchemaReferences))
            {
                var propertyName = AllocateUniqueName(
                    usedPropertyNames,
                    NormalizePascalIdentifier(property.Name, "Value"),
                    "Value");
                properties.Add(new SchemaPropertyDefinition(
                    property.Name,
                    propertyName,
                    ResolveTypeUsage(property.Schema, property.Required),
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
                baseTypeName,
                schema.Title ?? typeName,
                schema.Description,
                TryGetDictionaryValueType(schema, out var dictionaryValueType) ? dictionaryValueType : null,
                properties,
                discriminatorPropertyName,
                derivedTypes,
                enumKind,
                enumUnderlyingType,
                enumMembers);
        }

        private void RegisterOperationInlineSchemaNames()
        {
            foreach (var path in _document.Paths)
            {
                foreach (var operation in path.Value.Operations ?? [])
                {
                    var operationName = BuildOperationSchemaTypeName(
                        operation.Value.OperationId,
                        operation.Key.ToString(),
                        path.Key);

                    foreach (var parameter in CollectParameters(path.Value, operation.Value))
                    {
                        RegisterOperationParameterInlineSchema(parameter, operationName);
                    }

                    RegisterRequestBodyInlineSchema(operation.Value.RequestBody, operationName);
                    RegisterResponseInlineSchema(operation.Value, operationName);
                    RegisterErrorResponseInlineSchemas(operation.Value, operationName);
                }
            }
        }

        private void RegisterOperationParameterInlineSchema(IOpenApiParameter parameter, string operationName)
        {
            if (parameter.Schema is null || GetSchemaEnumKind(parameter.Schema) == SchemaEnumKind.None)
            {
                return;
            }

            RegisterOperationInlineSchema(parameter.Schema, operationName, parameter.Name ?? "Parameter");
        }

        private void RegisterRequestBodyInlineSchema(IOpenApiRequestBody? requestBody, string operationName)
        {
            if (requestBody is null || !TrySelectPreferredContent(requestBody.Content, GetRequestBodyContentPriority, out var selectedContent))
            {
                return;
            }

            RegisterOperationInlineSchema(selectedContent.Value.Schema, operationName, "Body");
        }

        private void RegisterResponseInlineSchema(OpenApiOperation operation, string operationName)
        {
            var response = SelectSuccessResponse(operation.Responses ?? []);
            if (response is null || !TrySelectPreferredContent(response.Content, GetResponseContentPriority, out var selectedContent))
            {
                return;
            }

            RegisterOperationInlineSchema(selectedContent.Value.Schema, operationName, "Response");
        }

        private void RegisterErrorResponseInlineSchemas(OpenApiOperation operation, string operationName)
        {
            if (operation.Responses is null || operation.Responses.Count == 0)
            {
                return;
            }

            foreach (var item in operation.Responses)
            {
                if (IsSuccessResponseStatus(item.Key)
                    || !TrySelectPreferredContent(item.Value.Content, GetErrorResponseContentPriority, out var selectedContent)
                    || !IsUsableErrorContent(selectedContent))
                {
                    continue;
                }

                RegisterOperationInlineSchema(selectedContent.Value.Schema, operationName, BuildErrorResponseChildName(item.Key));
            }
        }

        private static string BuildErrorResponseChildName(string statusCodePattern)
        {
            var suffix = statusCodePattern switch
            {
                var value when string.Equals(value, "default", StringComparison.OrdinalIgnoreCase) => "Default",
                _ => statusCodePattern.Replace('x', 'X')
            };

            return $"Error{suffix}Response";
        }

        private void RegisterOperationInlineSchema(IOpenApiSchema? schema, string operationName, string childName)
        {
            if (schema is null)
            {
                return;
            }

            var suggestedTypeName = BuildInlineSchemaTypeName(operationName, childName, schema);
            var inlineSchema = TryRegisterInlineSchema(schema, parentTypeName: null, suggestedTypeName);
            if (inlineSchema is not null)
            {
                RegisterNestedInlineSchemas(schema, new HashSet<string>(StringComparer.Ordinal), inlineSchema.TypeName, string.Empty);
                return;
            }

            RegisterNestedInlineSchemas(schema, new HashSet<string>(StringComparer.Ordinal), containingTypeName: null, operationName + ToPascalCase(childName));
        }

        private void RegisterNestedInlineSchemas(IOpenApiSchema? schema, HashSet<string> visited, string? containingTypeName, string nestedNamePrefix)
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
                    RegisterInlineSchemaChild(containingTypeName, nestedNamePrefix, property.Key, property.Value, visited);
                }
            }

            if (schema.Items is not null)
            {
                RegisterInlineSchemaChild(containingTypeName, nestedNamePrefix, "item", schema.Items, visited);
            }

            if (schema.AdditionalProperties is not null)
            {
                RegisterInlineSchemaChild(containingTypeName, nestedNamePrefix, "value", schema.AdditionalProperties, visited);
            }

            if (schema.PatternProperties is not null)
            {
                foreach (var patternProperty in schema.PatternProperties)
                {
                    RegisterInlineSchemaChild(containingTypeName, nestedNamePrefix, patternProperty.Key, patternProperty.Value, visited);
                }
            }

            if (schema.AllOf is not null)
            {
                foreach (var child in schema.AllOf)
                {
                    RegisterNestedInlineSchemas(child, visited, containingTypeName, nestedNamePrefix);
                }
            }

            if (schema.OneOf is not null)
            {
                foreach (var child in schema.OneOf)
                {
                    RegisterNestedInlineSchemas(child, visited, containingTypeName, nestedNamePrefix);
                }
            }

            if (schema.AnyOf is not null)
            {
                foreach (var child in schema.AnyOf)
                {
                    RegisterNestedInlineSchemas(child, visited, containingTypeName, nestedNamePrefix);
                }
            }

            visited.Remove(identity);
        }

        private void RegisterInlineSchemaChild(string? containingTypeName, string nestedNamePrefix, string childName, IOpenApiSchema? childSchema, HashSet<string> visited)
        {
            if (childSchema is null)
            {
                return;
            }

            var suggestedTypeName = BuildInlineSchemaTypeName(nestedNamePrefix, childName, childSchema);
            var inlineSchema = TryRegisterInlineSchema(childSchema, containingTypeName, suggestedTypeName);
            if (inlineSchema is not null)
            {
                RegisterNestedInlineSchemas(childSchema, visited, inlineSchema.TypeName, string.Empty);
                return;
            }

            var nextPrefix = CombineNestedTypeNamePrefix(nestedNamePrefix, childName);
            RegisterNestedInlineSchemas(childSchema, visited, containingTypeName, nextPrefix);
        }

        private InlineSchemaInfo? TryRegisterInlineSchema(IOpenApiSchema schema, string? parentTypeName, string suggestedTypeName)
        {
            if (!CanGenerateInlineSchema(schema))
            {
                return null;
            }

            var identity = GetSchemaIdentity(schema);
            if (_inlineSchemasByIdentity.TryGetValue(identity, out var existingInlineSchema))
            {
                return existingInlineSchema;
            }

            var inlineSchema = AllocateInlineSchema(parentTypeName, suggestedTypeName, schema);
            _inlineSchemasByIdentity.Add(identity, inlineSchema);
            _inlineSchemas.Add(inlineSchema);
            return inlineSchema;
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

        private InlineSchemaInfo AllocateInlineSchema(string? parentTypeName, string suggestedTypeName, IOpenApiSchema schema)
        {
            var declaredTypeName = AllocateTypeName(parentTypeName, NormalizePascalIdentifier(suggestedTypeName, "InlineObject"));
            var typeName = BuildQualifiedTypeName(parentTypeName, declaredTypeName);
            return new InlineSchemaInfo(typeName, declaredTypeName, parentTypeName, schema);
        }

        private static string BuildInlineSchemaTypeName(string nestedNamePrefix, string childName, IOpenApiSchema schema)
        {
            var suffix = childName switch
            {
                "item" when GetSchemaEnumKind(schema) != SchemaEnumKind.None => nestedNamePrefix.Length == 0 ? "ItemEnum" : "Item",
                "item" => nestedNamePrefix.Length == 0 ? "ItemModel" : "Item",
                "value" when GetSchemaEnumKind(schema) != SchemaEnumKind.None => nestedNamePrefix.Length == 0 ? "ValueEnum" : "Value",
                "value" => nestedNamePrefix.Length == 0 ? "ValueModel" : "Value",
                _ when GetSchemaEnumKind(schema) != SchemaEnumKind.None => ToPascalCase(childName) + "Enum",
                _ => ToPascalCase(childName) + "Model"
            };

            return NormalizePascalIdentifier(nestedNamePrefix + suffix, "InlineObject");
        }

        private static string CombineNestedTypeNamePrefix(string nestedNamePrefix, string childName)
        {
            var segment = childName switch
            {
                "item" => "Item",
                "value" => "Value",
                _ => ToPascalCase(childName)
            };

            return NormalizePascalIdentifier(nestedNamePrefix + segment, "InlineObject");
        }

        private static string BuildQualifiedTypeName(string? parentTypeName, string declaredTypeName)
            => string.IsNullOrWhiteSpace(parentTypeName) ? declaredTypeName : $"{parentTypeName}.{declaredTypeName}";

        private string AllocateTypeName(string? parentTypeName, string suggestedTypeName)
        {
            var baseTypeName = string.IsNullOrWhiteSpace(suggestedTypeName) ? "Model" : suggestedTypeName;
            var candidate = baseTypeName;
            var suffix = 2;

            while (!_usedSchemaTypeNames.Add(BuildQualifiedTypeName(parentTypeName, candidate)))
            {
                candidate = baseTypeName + suffix.ToString(CultureInfo.InvariantCulture);
                suffix++;
            }

            return candidate;
        }

        private TypeUsage ResolveTypeUsage(IOpenApiSchema? schema, bool required)
        {
            if (schema is null)
            {
                return new TypeUsage("string", TypeShape.String, schemaAllowsNull: false, isOptional: !required);
            }

            if (schema is IOpenApiReferenceHolder<JsonSchemaReference> { Reference.Id: not null } referenceHolder
                && _schemaNames.TryGetValue(referenceHolder.Reference.Id, out var schemaName))
            {
                return new TypeUsage(
                    schemaName,
                    GetTypeShape(ResolveSchemaReference(schema)),
                    SchemaAllowsNull(schema),
                    isOptional: !required);
            }

            if (_inlineSchemasByIdentity.TryGetValue(GetSchemaIdentity(schema), out var inlineSchema))
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

        private string ResolveTypeName(IOpenApiSchema? schema, bool required)
            => ResolveTypeUsage(schema, required).CSharpTypeName;

        private List<SchemaPropertyInfo> GetSchemaProperties(IOpenApiSchema schema, ISet<string>? ignoredPropertyNames = null, ISet<string>? ignoredSchemaReferences = null)
        {
            var properties = new List<SchemaPropertyInfo>();
            var indices = new Dictionary<string, int>(StringComparer.Ordinal);
            CollectSchemaProperties(schema, properties, indices, new HashSet<string>(StringComparer.Ordinal), ignoredPropertyNames, ignoredSchemaReferences);
            return properties;
        }

        private void CollectSchemaProperties(
            IOpenApiSchema schema,
            List<SchemaPropertyInfo> properties,
            Dictionary<string, int> indices,
            HashSet<string> visited,
            ISet<string>? ignoredPropertyNames,
            ISet<string>? ignoredSchemaReferences)
        {
            var schemaReferenceId = TryResolveSchemaReferenceId(schema);
            if (schemaReferenceId is not null && ignoredSchemaReferences?.Contains(schemaReferenceId) == true)
            {
                return;
            }

            var identity = GetSchemaIdentity(schema);
            if (!visited.Add(identity))
            {
                return;
            }

            if (schema.AllOf is not null)
            {
                foreach (var child in schema.AllOf)
                {
                    CollectSchemaProperties(child, properties, indices, visited, ignoredPropertyNames, ignoredSchemaReferences);
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
            if (TryResolveSchemaReferenceId(schema) is { } schemaReferenceId
                && _schemaNames.TryGetValue(schemaReferenceId, out var schemaName))
            {
                return schemaName;
            }

            return null;
        }

        private static string? TryResolveSchemaReferenceId(IOpenApiSchema? schema)
        {
            return schema is IOpenApiReferenceHolder<JsonSchemaReference> { Reference.Id: not null } referenceHolder
                ? referenceHolder.Reference.Id
                : null;
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

        private TypeShape GetTypeShape(IOpenApiSchema schema)
        {
            var resolvedSchema = ResolveSchemaReference(schema);
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

        private bool SchemaAllowsNull(IOpenApiSchema schema)
        {
            if (HasSchemaType(schema, JsonSchemaType.Null) || SchemaCompositionsAllowNull(schema))
            {
                return true;
            }

            var resolvedSchema = ResolveSchemaReference(schema);
            return !ReferenceEquals(resolvedSchema, schema)
                && (HasSchemaType(resolvedSchema, JsonSchemaType.Null) || SchemaCompositionsAllowNull(resolvedSchema));
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

        private sealed class InlineSchemaInfo(string typeName, string declaredTypeName, string? parentTypeName, IOpenApiSchema schema)
        {
            public string TypeName { get; } = typeName;
            public string DeclaredTypeName { get; } = declaredTypeName;
            public string? ParentTypeName { get; } = parentTypeName;
            public IOpenApiSchema Schema { get; } = schema;
        }

        private sealed class PolymorphicSchemaInfo(string discriminatorPropertyName, IReadOnlyList<SchemaDerivedTypeDefinition> derivedTypes)
        {
            public string DiscriminatorPropertyName { get; } = discriminatorPropertyName;
            public IReadOnlyList<SchemaDerivedTypeDefinition> DerivedTypes { get; } = derivedTypes;
        }

        private sealed class PolymorphicDerivedSchemaInfo(string baseSchemaName, string baseTypeName, string discriminatorPropertyName)
        {
            public string BaseSchemaName { get; } = baseSchemaName;
            public string BaseTypeName { get; } = baseTypeName;
            public string DiscriminatorPropertyName { get; } = discriminatorPropertyName;
        }

        private sealed class SchemaPropertyInfo(string name, IOpenApiSchema schema, bool required)
        {
            public string Name { get; } = name;
            public IOpenApiSchema Schema { get; } = schema;
            public bool Required { get; } = required;
        }
    }
}
