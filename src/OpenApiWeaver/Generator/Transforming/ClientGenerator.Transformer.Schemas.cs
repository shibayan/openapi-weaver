using Microsoft.OpenApi;

namespace OpenApiWeaver;

public sealed partial class ClientGenerator
{
    private sealed partial class Transformer
    {
        private void RegisterSchemaNames()
        {
            _schemaCatalog.ReserveTypeName(_clientName);
            SupportTypeNames.ReserveTypeNames(_schemaCatalog);

            if (_document.Components?.Schemas is null)
            {
                return;
            }

            foreach (var schema in _document.Components.Schemas)
            {
                var schemaName = _schemaCatalog.AllocateTypeName(parentTypeName: null, NormalizePascalIdentifier(schema.Key, "Model"));
                _schemaCatalog.AddComponentSchemaName(schema.Key, schemaName);
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
                throw new UnsupportedGenerationException(
                    $"Schema '{schemaName}' uses discriminator with anyOf, which is not supported for compile-time code generation.",
                    UnsupportedFeatureKind.Discriminator);
            }

            if (schema.OneOf is not { Count: > 0 })
            {
                throw new UnsupportedGenerationException(
                    $"Schema '{schemaName}' uses discriminator without oneOf, which is not supported for compile-time code generation.",
                    UnsupportedFeatureKind.Discriminator);
            }

            var discriminatorPropertyName = schema.Discriminator?.PropertyName;
            if (string.IsNullOrWhiteSpace(discriminatorPropertyName))
            {
                throw new UnsupportedGenerationException(
                    $"Schema '{schemaName}' uses discriminator without a propertyName, which is not supported for compile-time code generation.",
                    UnsupportedFeatureKind.Discriminator);
            }

            var baseTypeName = _schemaCatalog.GetComponentSchemaName(schemaName);
            var derivedSchemaNames = new HashSet<string>(StringComparer.Ordinal);
            var derivedTypes = new List<SchemaDerivedTypeDefinition>();
            var pendingDerivedSchemas = new List<(string TypeName, PolymorphicDerivedSchemaInfo Info)>();
            var usedDiscriminatorValues = new HashSet<string>(StringComparer.Ordinal);
            foreach (var child in schema.OneOf)
            {
                var derivedSchemaName = SchemaReferenceResolver.TryResolveSchemaReferenceId(child);
                if (derivedSchemaName is null)
                {
                    throw new UnsupportedGenerationException(
                        $"Schema '{schemaName}' uses discriminator with inline oneOf members, which is not supported for compile-time code generation.",
                        UnsupportedFeatureKind.Discriminator);
                }

                derivedSchemaNames.Add(derivedSchemaName);

                if (!_schemaCatalog.TryGetComponentSchemaName(derivedSchemaName, out var derivedTypeName))
                {
                    throw new UnsupportedGenerationException(
                        $"Schema '{schemaName}' discriminator references unknown schema '{derivedSchemaName}'.",
                        UnsupportedFeatureKind.Discriminator);
                }

                var discriminatorValue = ResolveDiscriminatorValue(schema.Discriminator!, derivedSchemaName);
                if (!usedDiscriminatorValues.Add(discriminatorValue))
                {
                    throw new UnsupportedGenerationException(
                        $"Schema '{schemaName}' uses duplicate discriminator value '{discriminatorValue}', which is not supported for compile-time code generation.",
                        UnsupportedFeatureKind.Discriminator);
                }

                if (_polymorphicDerivedSchemasByTypeName.TryGetValue(derivedTypeName, out var existingDerivedSchema)
                    && !string.Equals(existingDerivedSchema.BaseTypeName, baseTypeName, StringComparison.Ordinal))
                {
                    throw new UnsupportedGenerationException(
                        $"Schema '{derivedSchemaName}' is used by multiple discriminator hierarchies, which is not supported for compile-time code generation.",
                        UnsupportedFeatureKind.Discriminator);
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
                    if (string.Equals(SchemaReferenceResolver.TryResolveSchemaReferenceId(mapping.Value), derivedSchemaName, StringComparison.Ordinal))
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
                var mappedSchemaName = SchemaReferenceResolver.TryResolveSchemaReferenceId(mapping.Value);
                if (mappedSchemaName is null || !derivedSchemaNames.Contains(mappedSchemaName))
                {
                    throw new UnsupportedGenerationException(
                        $"Schema '{schemaName}' discriminator mapping '{mapping.Key}' must reference a schema listed in oneOf.",
                        UnsupportedFeatureKind.Discriminator);
                }
            }
        }

        private void RegisterInlineSchemaNames()
        {
            if (_document.Components?.Schemas is not null)
            {
                foreach (var schema in _document.Components.Schemas)
                {
                    var schemaName = _schemaCatalog.GetComponentSchemaName(schema.Key);
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
                    var schemaName = _schemaCatalog.GetComponentSchemaName(schema.Key);
                    var definition = CreateSchemaDefinition(schemaName, schemaName, parentTypeName: null, schema.Value);
                    schemas.Add(definition);
                    _schemaDefinitionsByTypeName[definition.QualifiedTypeName] = definition;
                }
            }

            foreach (var inlineSchema in _schemaCatalog.InlineSchemas)
            {
                var definition = CreateSchemaDefinition(inlineSchema.TypeName, inlineSchema.DeclaredTypeName, inlineSchema.ParentTypeName, inlineSchema.Schema);
                schemas.Add(definition);
                _schemaDefinitionsByTypeName[definition.QualifiedTypeName] = definition;
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
            foreach (var property in SchemaPropertyCollector.Collect(schema, ignoredPropertyNames, ignoredSchemaReferences))
            {
                if (property.ReadOnly && property.WriteOnly)
                {
                    throw new UnsupportedGenerationException(
                        $"Schema '{typeName}' property '{property.Name}' is marked both readOnly and writeOnly, which is not supported.",
                        UnsupportedFeatureKind.Schema);
                }

                var propertyName = AllocateUniqueName(
                    usedPropertyNames,
                    NormalizePascalIdentifier(property.Name, "Value"),
                    "Value");
                properties.Add(new SchemaPropertyDefinition(
                    property.Name,
                    propertyName,
                    _schemaTypeResolver.ResolveTypeUsage(property.Schema, property.Required),
                    property.Required,
                    property.ReadOnly,
                    property.WriteOnly,
                    property.Schema.Title ?? propertyName,
                    property.Schema.Description));
            }

            var enumKind = _schemaEnumResolver.GetSchemaEnumKind(schema);
            var enumUnderlyingType = enumKind switch
            {
                SchemaEnumKind.Integer => SchemaEnumResolver.GetIntegerEnumUnderlyingType(schema),
                SchemaEnumKind.Number => SchemaEnumResolver.GetNumberEnumValueType(schema),
                _ => null
            };
            var enumMembers = enumKind == SchemaEnumKind.None ? [] : SchemaEnumResolver.CreateEnumMembers(schema, enumKind);

            return new SchemaDefinition(
                typeName,
                declaredTypeName,
                parentTypeName,
                baseTypeName,
                schema.Title ?? typeName,
                schema.Description,
                _schemaTypeResolver.TryGetDictionaryValueType(schema, out var dictionaryValueType) ? dictionaryValueType : null,
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
            if (parameter.Schema is null || _schemaEnumResolver.GetSchemaEnumKind(parameter.Schema) == SchemaEnumKind.None)
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

            var hasSuccessStatus = operation.Responses.Any(static item => IsSuccessResponseStatus(item.Key));

            foreach (var item in operation.Responses)
            {
                if (IsSuccessResponseStatus(item.Key))
                {
                    continue;
                }

                if (!hasSuccessStatus && string.Equals(item.Key, "default", StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                if (!TrySelectPreferredContent(item.Value.Content, GetErrorResponseContentPriority, out var selectedContent)
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

            RegisterNestedInlineSchemas(schema, new HashSet<string>(StringComparer.Ordinal), containingTypeName: null, operationName + CSharpUtilities.ToPascalCase(childName));
        }

        private void RegisterNestedInlineSchemas(IOpenApiSchema? schema, HashSet<string> visited, string? containingTypeName, string nestedNamePrefix)
        {
            if (schema is null || _schemaReferenceResolver.TryResolveSchemaReferenceName(schema) is not null)
            {
                return;
            }

            var identity = SchemaReferenceResolver.GetSchemaIdentity(schema);
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

            var identity = SchemaReferenceResolver.GetSchemaIdentity(schema);
            if (_schemaCatalog.TryGetInlineSchema(identity, out var existingInlineSchema))
            {
                return existingInlineSchema;
            }

            return _schemaCatalog.AddInlineSchema(
                identity,
                parentTypeName,
                NormalizePascalIdentifier(suggestedTypeName, "InlineObject"),
                schema);
        }

        private bool CanGenerateInlineSchema(IOpenApiSchema schema)
        {
            if (_schemaReferenceResolver.TryResolveSchemaReferenceName(schema) is not null
                || SchemaTypeResolver.IsDictionarySchema(schema)
                || schema.OneOf is { Count: > 0 }
                || schema.AnyOf is { Count: > 0 })
            {
                return false;
            }

            var baseType = schema.Type & ~JsonSchemaType.Null;
            return _schemaEnumResolver.IsEnumSchema(schema)
                || baseType == JsonSchemaType.Object
                || schema.AllOf is { Count: > 0 }
                || (schema.Properties?.Count ?? 0) > 0;
        }

        private string BuildInlineSchemaTypeName(string nestedNamePrefix, string childName, IOpenApiSchema schema)
        {
            var suffix = childName switch
            {
                "item" when _schemaEnumResolver.GetSchemaEnumKind(schema) != SchemaEnumKind.None => nestedNamePrefix.Length == 0 ? "ItemEnum" : "Item",
                "item" => nestedNamePrefix.Length == 0 ? "ItemModel" : "Item",
                "value" when _schemaEnumResolver.GetSchemaEnumKind(schema) != SchemaEnumKind.None => nestedNamePrefix.Length == 0 ? "ValueEnum" : "Value",
                "value" => nestedNamePrefix.Length == 0 ? "ValueModel" : "Value",
                _ when _schemaEnumResolver.GetSchemaEnumKind(schema) != SchemaEnumKind.None => CSharpUtilities.ToPascalCase(childName) + "Enum",
                _ => CSharpUtilities.ToPascalCase(childName) + "Model"
            };

            return NormalizePascalIdentifier(nestedNamePrefix + suffix, "InlineObject");
        }

        private static string CombineNestedTypeNamePrefix(string nestedNamePrefix, string childName)
        {
            var segment = childName switch
            {
                "item" => "Item",
                "value" => "Value",
                _ => CSharpUtilities.ToPascalCase(childName)
            };

            return NormalizePascalIdentifier(nestedNamePrefix + segment, "InlineObject");
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

    }
}
