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
                // Aliases such as `UserId: { type: string }` resolve to their underlying type at each use site.
                if (!GeneratesComponentType(schema.Value))
                {
                    continue;
                }

                var schemaName = _schemaCatalog.AllocateTypeName(parentTypeName: null, NormalizePascalIdentifier(schema.Key, "Model"), GetCompanionTypeSuffix(schema.Value));
                _schemaCatalog.AddComponentSchemaName(schema.Key, schemaName);
            }
        }

        private bool GeneratesComponentType(IOpenApiSchema schema)
        {
            return schema.Discriminator is not null
                || _schemaEnumResolver.IsEnumSchema(schema)
                || ComposesObject(schema, new HashSet<string>(StringComparer.Ordinal));
        }

        private static bool ComposesObject(IOpenApiSchema schema, HashSet<string> visited)
        {
            if (!visited.Add(SchemaReferenceResolver.GetSchemaIdentity(schema)))
            {
                return false;
            }

            // A discriminator base is an object even without `type` or `properties`, so a subtype that only
            // extends it (`Cat: { allOf: [$ref: Pet] }`) still gets a model.
            if ((schema.Type & ~JsonSchemaType.Null) == JsonSchemaType.Object
                || (schema.Properties?.Count ?? 0) > 0
                || schema.Discriminator is not null
                || SchemaTypeResolver.IsDictionarySchema(schema))
            {
                return true;
            }

            return schema.AllOf?.Any(child => ComposesObject(child, visited)) == true;
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

            // A subtype that roots its own hierarchy would need two discriminator contracts on one type, which cannot round-trip.
            foreach (var derivedTypeName in _polymorphicDerivedSchemasByTypeName.Keys)
            {
                if (_polymorphicSchemasByTypeName.ContainsKey(derivedTypeName))
                {
                    throw new UnsupportedGenerationException(
                        $"Schema '{derivedTypeName}' is a discriminator subtype that declares its own discriminator hierarchy, which is not supported for compile-time code generation.",
                        UnsupportedFeatureKind.Discriminator);
                }
            }
        }

        private void RegisterPolymorphicSchemaInfo(string schemaName, IOpenApiSchema schema)
        {
            var discriminatorPropertyName = schema.Discriminator?.PropertyName;
            if (string.IsNullOrWhiteSpace(discriminatorPropertyName))
            {
                throw new UnsupportedGenerationException(
                    $"Schema '{schemaName}' uses discriminator without a propertyName, which is not supported for compile-time code generation.",
                    UnsupportedFeatureKind.Discriminator);
            }

            var (memberKeyword, subtypes) = ResolveDiscriminatorMembers(schemaName, schema);
            if (subtypes.Count == 0)
            {
                // Nothing derives from this schema, so the discriminator is an ordinary property.
                return;
            }

            // Dictionary-backed models are emitted as `Dictionary<,>` subclasses and cannot also take part in a class hierarchy.
            foreach (var hierarchySchemaName in subtypes.Select(static subtype => subtype.SchemaName).Prepend(schemaName))
            {
                if (_document.Components!.Schemas!.TryGetValue(hierarchySchemaName, out var hierarchySchema)
                    && SchemaTypeResolver.IsDictionarySchema(hierarchySchema))
                {
                    throw new UnsupportedGenerationException(
                        $"Schema '{hierarchySchemaName}' uses additionalProperties or patternProperties in the '{schemaName}' discriminator hierarchy, which is not supported for compile-time code generation.",
                        UnsupportedFeatureKind.Discriminator);
                }
            }

            var baseTypeName = _schemaCatalog.GetComponentSchemaName(schemaName);
            var derivedSchemaNames = new HashSet<string>(StringComparer.Ordinal);
            var derivedTypes = new List<SchemaDerivedTypeDefinition>();
            var pendingDerivedSchemas = new Dictionary<string, (string TypeName, PolymorphicDerivedSchemaInfo Info)>(StringComparer.Ordinal);
            var usedDiscriminatorValues = new HashSet<string>(StringComparer.Ordinal);
            foreach (var (derivedSchemaName, parentSchemaName) in subtypes)
            {
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
                    && !string.Equals(existingDerivedSchema.RootTypeName, baseTypeName, StringComparison.Ordinal))
                {
                    throw new UnsupportedGenerationException(
                        $"Schema '{derivedSchemaName}' is used by multiple discriminator hierarchies, which is not supported for compile-time code generation.",
                        UnsupportedFeatureKind.Discriminator);
                }

                // Subtypes are listed parents first, so an intermediate parent is already pending here.
                var parentTypeName = string.Equals(parentSchemaName, schemaName, StringComparison.Ordinal)
                    ? baseTypeName
                    : pendingDerivedSchemas[parentSchemaName].TypeName;
                var derivedInfo = new PolymorphicDerivedSchemaInfo(parentSchemaName, parentTypeName, baseTypeName, discriminatorPropertyName!);
                var derivedType = new SchemaDerivedTypeDefinition(derivedTypeName, discriminatorValue);
                derivedInfo.SelfAndDescendantTypes.Add(derivedType);
                pendingDerivedSchemas[derivedSchemaName] = (derivedTypeName, derivedInfo);
                derivedTypes.Add(derivedType);

                // An intermediate type must also register its descendants, or serializing them through it fails.
                var ancestorSchemaName = parentSchemaName;
                while (pendingDerivedSchemas.TryGetValue(ancestorSchemaName, out var ancestor))
                {
                    ancestor.Info.SelfAndDescendantTypes.Add(derivedType);
                    ancestorSchemaName = ancestor.Info.BaseSchemaName;
                }
            }

            ValidateDiscriminatorMappings(schemaName, schema.Discriminator!, derivedSchemaNames, memberKeyword);

            foreach (var (derivedTypeName, derivedInfo) in pendingDerivedSchemas.Values)
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

        private static void ValidateDiscriminatorMappings(string schemaName, OpenApiDiscriminator discriminator, HashSet<string> derivedSchemaNames, string derivedSchemaDescription)
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
                        $"Schema '{schemaName}' discriminator mapping '{mapping.Key}' must reference {derivedSchemaDescription}.",
                        UnsupportedFeatureKind.Discriminator);
                }
            }
        }

        // Returns each subtype with the schema it directly extends, parents before their own subtypes.
        private (string DerivedSchemaDescription, List<(string SchemaName, string ParentSchemaName)> Subtypes) ResolveDiscriminatorMembers(string schemaName, IOpenApiSchema schema)
        {
            var keyword = schema.OneOf is { Count: > 0 } ? "oneOf" : "anyOf";
            if ((schema.OneOf is { Count: > 0 } ? schema.OneOf : schema.AnyOf) is { Count: > 0 } members)
            {
                var memberSubtypes = new List<(string, string)>(members.Count);
                foreach (var member in members)
                {
                    var memberName = SchemaReferenceResolver.TryResolveSchemaReferenceId(member)
                        ?? throw new UnsupportedGenerationException(
                            $"Schema '{schemaName}' uses discriminator with inline {keyword} members, which is not supported for compile-time code generation.",
                            UnsupportedFeatureKind.Discriminator);

                    // A schema cannot be its own subtype; it would also never terminate the ancestor walk.
                    if (string.Equals(memberName, schemaName, StringComparison.Ordinal))
                    {
                        throw new UnsupportedGenerationException(
                            $"Schema '{schemaName}' lists itself as a discriminator {keyword} member, which is not supported for compile-time code generation.",
                            UnsupportedFeatureKind.Discriminator);
                    }

                    memberSubtypes.Add((memberName, schemaName));
                }

                return ($"a schema listed in {keyword}", memberSubtypes);
            }

            // OpenAPI 3.0 inheritance: the base declares the discriminator and subtypes extend it, directly or through
            // other subtypes, with `allOf: [$ref: parent]`.
            var componentSchemas = _document.Components!.Schemas!;
            List<string> GetReferencedSchemaNames(string componentName)
                => componentSchemas[componentName].AllOf?
                    .Select(static child => SchemaReferenceResolver.TryResolveSchemaReferenceId(child))
                    .OfType<string>()
                    .Where(name => !string.Equals(name, componentName, StringComparison.Ordinal))
                    .ToList() ?? [];

            var hierarchyMembers = new List<string>();
            var hierarchy = new HashSet<string>(StringComparer.Ordinal) { schemaName };
            var parents = new Queue<string>();
            parents.Enqueue(schemaName);
            while (parents.Count > 0)
            {
                var parentSchemaName = parents.Dequeue();
                foreach (var component in componentSchemas)
                {
                    if (hierarchy.Contains(component.Key) || !GetReferencedSchemaNames(component.Key).Contains(parentSchemaName))
                    {
                        continue;
                    }

                    hierarchy.Add(component.Key);
                    hierarchyMembers.Add(component.Key);

                    // A subtype that declares its own discriminator roots a separate hierarchy for its descendants.
                    if (component.Value.Discriminator is null)
                    {
                        parents.Enqueue(component.Key);
                    }
                }
            }

            // A subtype extends the most specific hierarchy member it references, so a redundant reference to an ancestor
            // (`Siamese: allOf: [$ref: Pet, $ref: Cat]`) still makes it a Cat. Unrelated parents cannot be represented.
            var parentByMember = new Dictionary<string, string>(StringComparer.Ordinal);
            var depthByMember = new Dictionary<string, int>(StringComparer.Ordinal) { [schemaName] = 0 };
            bool IsAncestor(string ancestorName, string memberName)
            {
                while (!string.Equals(memberName, schemaName, StringComparison.Ordinal))
                {
                    memberName = parentByMember[memberName];
                    if (string.Equals(memberName, ancestorName, StringComparison.Ordinal))
                    {
                        return true;
                    }
                }

                return false;
            }

            int GetDepth(string memberName, HashSet<string> resolving)
            {
                if (depthByMember.TryGetValue(memberName, out var knownDepth))
                {
                    return knownDepth;
                }

                if (!resolving.Add(memberName))
                {
                    throw new UnsupportedGenerationException(
                        $"Schema '{memberName}' is part of a cyclic allOf inheritance chain in the '{schemaName}' discriminator hierarchy, which is not supported for compile-time code generation.",
                        UnsupportedFeatureKind.Discriminator);
                }

                var referencedMembers = GetReferencedSchemaNames(memberName).Where(hierarchy.Contains).Distinct(StringComparer.Ordinal).ToList();
                var parentName = referencedMembers.OrderByDescending(name => GetDepth(name, resolving)).First();
                parentByMember[memberName] = parentName;
                foreach (var referencedMember in referencedMembers)
                {
                    if (!string.Equals(referencedMember, parentName, StringComparison.Ordinal) && !IsAncestor(referencedMember, memberName))
                    {
                        throw new UnsupportedGenerationException(
                            $"Schema '{memberName}' extends both '{parentName}' and '{referencedMember}' in the '{schemaName}' discriminator hierarchy, which is not supported for compile-time code generation.",
                            UnsupportedFeatureKind.Discriminator);
                    }
                }

                resolving.Remove(memberName);
                depthByMember[memberName] = depthByMember[parentName] + 1;
                return depthByMember[memberName];
            }

            var subtypes = hierarchyMembers
                .OrderBy(member => GetDepth(member, new HashSet<string>(StringComparer.Ordinal)))
                .Select(member => (member, parentByMember[member]))
                .ToList();
            return ("a schema that extends it through allOf", subtypes);
        }

        private void RegisterInlineSchemaNames()
        {
            if (_document.Components?.Schemas is not null)
            {
                // Bases are visited before their subtypes: a subtype's nested types must avoid the member names it inherits,
                // otherwise a nested type such as `Cat.DetailModel` would hide an inherited `DetailModel` property (CS9031).
                var components = _document.Components.Schemas.ToList();
                foreach (var schema in components.OrderBy(component => _schemaCatalog.TryGetComponentSchemaName(component.Key, out var typeName) ? GetInheritanceDepth(typeName) : 0))
                {
                    if (_schemaCatalog.TryGetComponentSchemaName(schema.Key, out var schemaName))
                    {
                        _componentSchemasByTypeName[schemaName] = schema.Value;
                        foreach (var inheritedMemberName in GetInheritedPropertyNames(schemaName).Values)
                        {
                            _schemaCatalog.ReserveTypeName($"{schemaName}.{inheritedMemberName}");
                        }

                        RegisterNestedInlineSchemas(schema.Value, new HashSet<string>(StringComparer.Ordinal), schemaName, string.Empty);
                    }
                    else
                    {
                        RegisterNestedInlineSchemas(schema.Value, new HashSet<string>(StringComparer.Ordinal), containingTypeName: null, NormalizePascalIdentifier(schema.Key, "Model"));
                    }
                }
            }

            RegisterOperationInlineSchemaNames();
        }

        private List<SchemaDefinition> BuildSchemaDefinitions()
        {
            var schemas = new List<SchemaDefinition>();

            if (_document.Components?.Schemas is not null)
            {
                var components = new List<(string TypeName, IOpenApiSchema Schema)>();
                foreach (var schema in _document.Components.Schemas)
                {
                    if (_schemaCatalog.TryGetComponentSchemaName(schema.Key, out var schemaName))
                    {
                        components.Add((schemaName, schema.Value));
                    }
                }

                // Bases are built before their subtypes so a subtype can merge the members it inherits.
                foreach (var (typeName, schema) in components.OrderBy(component => GetInheritanceDepth(component.TypeName)))
                {
                    var definition = CreateSchemaDefinition(typeName, typeName, parentTypeName: null, schema);
                    _schemaDefinitionsByTypeName[definition.QualifiedTypeName] = definition;
                    _schemaSourcesByTypeName[definition.QualifiedTypeName] = schema;
                }

                schemas.AddRange(components.Select(component => _schemaDefinitionsByTypeName[component.TypeName]));
            }

            foreach (var inlineSchema in _schemaCatalog.InlineSchemas)
            {
                var definition = CreateSchemaDefinition(inlineSchema.TypeName, inlineSchema.DeclaredTypeName, inlineSchema.ParentTypeName, inlineSchema.Schema);
                schemas.Add(definition);
                _schemaDefinitionsByTypeName[definition.QualifiedTypeName] = definition;
                _schemaSourcesByTypeName[definition.QualifiedTypeName] = inlineSchema.Schema;
            }

            return schemas;
        }

        private int GetInheritanceDepth(string typeName)
        {
            var depth = 0;
            while (_polymorphicDerivedSchemasByTypeName.TryGetValue(typeName, out var derivedInfo))
            {
                depth++;
                typeName = derivedInfo.BaseTypeName;
            }

            return depth;
        }

        // The discriminator is never a CLR property, and a subtype does not collect the members of the schema it extends.
        private (HashSet<string> IgnoredPropertyNames, HashSet<string> IgnoredSchemaReferences) GetPropertyCollectionFilters(string typeName)
        {
            var ignoredPropertyNames = new HashSet<string>(StringComparer.Ordinal);
            var ignoredSchemaReferences = new HashSet<string>(StringComparer.Ordinal);
            if (_polymorphicSchemasByTypeName.TryGetValue(typeName, out var polymorphicSchema))
            {
                ignoredPropertyNames.Add(polymorphicSchema.DiscriminatorPropertyName);
            }

            if (_polymorphicDerivedSchemasByTypeName.TryGetValue(typeName, out var polymorphicDerivedSchema))
            {
                ignoredPropertyNames.Add(polymorphicDerivedSchema.DiscriminatorPropertyName);
                ignoredSchemaReferences.Add(polymorphicDerivedSchema.BaseSchemaName);
            }

            return (ignoredPropertyNames, ignoredSchemaReferences);
        }

        // CLR names of a type's own properties keyed by JSON name. They are allocated once, after the type's nested types
        // are named, so subtypes can reserve the exact names (including collision suffixes) they inherit.
        private Dictionary<string, string> GetPropertyNamePlan(string typeName, string declaredTypeName, IOpenApiSchema schema)
        {
            if (_propertyNamePlansByTypeName.TryGetValue(typeName, out var cachedPlan))
            {
                return cachedPlan;
            }

            var (ignoredPropertyNames, ignoredSchemaReferences) = GetPropertyCollectionFilters(typeName);
            var inheritedPropertyNames = GetInheritedPropertyNames(typeName);

            // A member cannot share the name of its enclosing type (CS0542), of a nested type or its companion converter
            // (CS0102), or of an inherited member it would hide (CS0108 / CS9031).
            var usedPropertyNames = new HashSet<string>(
                _schemaCatalog.InlineSchemas
                    .Where(inlineSchema => string.Equals(inlineSchema.ParentTypeName, typeName, StringComparison.Ordinal))
                    .SelectMany(static inlineSchema => inlineSchema.CompanionTypeName is { } companionTypeName
                        ? new[] { inlineSchema.DeclaredTypeName, companionTypeName }
                        : [inlineSchema.DeclaredTypeName])
                    .Concat(inheritedPropertyNames.Values)
                    .Append(declaredTypeName),
                StringComparer.Ordinal);
            var plan = new Dictionary<string, string>(StringComparer.Ordinal);
            foreach (var property in SchemaPropertyCollector.Collect(schema, ignoredPropertyNames, ignoredSchemaReferences))
            {
                // A redeclared inherited property reuses the inherited member.
                if (!inheritedPropertyNames.ContainsKey(property.Name))
                {
                    plan[property.Name] = AllocateUniqueName(usedPropertyNames, NormalizePascalIdentifier(property.Name, "Value"), "Value");
                }
            }

            _propertyNamePlansByTypeName[typeName] = plan;
            return plan;
        }

        private Dictionary<string, string> GetInheritedPropertyNames(string typeName)
        {
            var inheritedPropertyNames = new Dictionary<string, string>(StringComparer.Ordinal);
            while (_polymorphicDerivedSchemasByTypeName.TryGetValue(typeName, out var derivedInfo)
                && _componentSchemasByTypeName.TryGetValue(derivedInfo.BaseTypeName, out var baseSchema))
            {
                foreach (var plannedName in GetPropertyNamePlan(derivedInfo.BaseTypeName, derivedInfo.BaseTypeName, baseSchema))
                {
                    if (!inheritedPropertyNames.ContainsKey(plannedName.Key))
                    {
                        inheritedPropertyNames.Add(plannedName.Key, plannedName.Value);
                    }
                }

                typeName = derivedInfo.BaseTypeName;
            }

            return inheritedPropertyNames;
        }

        // Members a subtype inherits through CLR inheritance, keyed by JSON name, with the readOnly / writeOnly / required
        // constraints every ancestor applies to them, including those added by intermediate subtypes.
        private Dictionary<string, InheritedProperty> CollectInheritedProperties(string? baseTypeName)
        {
            var chain = new List<SchemaDefinition>();
            while (baseTypeName is not null && _schemaDefinitionsByTypeName.TryGetValue(baseTypeName, out var baseDefinition))
            {
                chain.Add(baseDefinition);
                baseTypeName = baseDefinition.BaseTypeName;
            }

            var rules = chain.SelectMany(static definition => definition.InheritedPropertyRules).ToList();
            var inheritedProperties = new Dictionary<string, InheritedProperty>(StringComparer.Ordinal);
            foreach (var property in chain.SelectMany(static definition => definition.Properties))
            {
                if (inheritedProperties.ContainsKey(property.JsonPropertyName))
                {
                    continue;
                }

                bool HasRule(InheritedPropertyRuleKind kind)
                    => rules.Any(rule => rule.Kind == kind && string.Equals(rule.JsonPropertyName, property.JsonPropertyName, StringComparison.Ordinal));

                inheritedProperties.Add(property.JsonPropertyName, new InheritedProperty(
                    property,
                    property.ReadOnly || HasRule(InheritedPropertyRuleKind.IgnoreInRequest),
                    property.WriteOnly || HasRule(InheritedPropertyRuleKind.IgnoreInResponse),
                    property.Required || HasRule(InheritedPropertyRuleKind.RequireInResponse)));
            }

            return inheritedProperties;
        }

        private SchemaDefinition CreateSchemaDefinition(string typeName, string declaredTypeName, string? parentTypeName, IOpenApiSchema schema)
        {
            var (ignoredPropertyNames, ignoredSchemaReferences) = GetPropertyCollectionFilters(typeName);
            string? baseTypeName = null;
            string? discriminatorPropertyName = null;
            IReadOnlyList<SchemaDerivedTypeDefinition> derivedTypes = [];

            if (_polymorphicSchemasByTypeName.TryGetValue(typeName, out var polymorphicSchema))
            {
                discriminatorPropertyName = polymorphicSchema.DiscriminatorPropertyName;
                derivedTypes = polymorphicSchema.DerivedTypes;
            }

            if (_polymorphicDerivedSchemasByTypeName.TryGetValue(typeName, out var polymorphicDerivedSchema))
            {
                baseTypeName = polymorphicDerivedSchema.BaseTypeName;
            }

            var properties = new List<SchemaPropertyDefinition>();
            var propertyNamePlan = GetPropertyNamePlan(typeName, declaredTypeName, schema);
            var inheritedProperties = CollectInheritedProperties(baseTypeName);
            var inheritedPropertyRules = new List<InheritedPropertyRule>();
            var redeclaredWriteOnlyNames = new HashSet<string>(StringComparer.Ordinal);
            foreach (var property in SchemaPropertyCollector.Collect(schema, ignoredPropertyNames, ignoredSchemaReferences))
            {
                if (property.ReadOnly && property.WriteOnly)
                {
                    throw new UnsupportedGenerationException(
                        $"Schema '{typeName}' property '{property.Name}' is marked both readOnly and writeOnly, which is not supported.",
                        UnsupportedFeatureKind.Schema);
                }

                if (inheritedProperties.TryGetValue(property.Name, out var inheritedProperty))
                {
                    // A subtype that redeclares an inherited property reuses the inherited member.
                    var inheritedTypeName = inheritedProperty.Property.Type.NonNullableCSharpTypeName;
                    var redeclaredTypeName = _schemaTypeResolver.ResolveTypeUsage(property.Schema, required: true).NonNullableCSharpTypeName;
                    if (!string.Equals(redeclaredTypeName, inheritedTypeName, StringComparison.Ordinal))
                    {
                        throw new UnsupportedGenerationException(
                            $"Schema '{typeName}' redeclares inherited property '{property.Name}' as '{redeclaredTypeName}' instead of '{inheritedTypeName}', which is not supported for compile-time code generation.",
                            UnsupportedFeatureKind.Discriminator);
                    }

                    // Direction annotations the subtype adds still apply to its own contract.
                    if ((property.ReadOnly || inheritedProperty.ReadOnly) && (property.WriteOnly || inheritedProperty.WriteOnly))
                    {
                        throw new UnsupportedGenerationException(
                            $"Schema '{typeName}' property '{property.Name}' is marked both readOnly and writeOnly through inheritance, which is not supported.",
                            UnsupportedFeatureKind.Schema);
                    }

                    if (property.WriteOnly && !inheritedProperty.WriteOnly)
                    {
                        redeclaredWriteOnlyNames.Add(property.Name);
                        inheritedPropertyRules.Add(new InheritedPropertyRule(property.Name, InheritedPropertyRuleKind.IgnoreInResponse));
                    }

                    if (property.ReadOnly && !inheritedProperty.ReadOnly)
                    {
                        inheritedPropertyRules.Add(new InheritedPropertyRule(property.Name, InheritedPropertyRuleKind.IgnoreInRequest));
                    }

                    continue;
                }

                var propertyName = propertyNamePlan[property.Name];
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

            // A subtype can make an inherited optional property required; the inherited CLR member cannot change,
            // so the requirement is enforced by the response serializer contract instead. A property that is
            // writeOnly anywhere in the ancestry is never present in responses, so it is not required there.
            if (inheritedProperties.Count > 0)
            {
                foreach (var requiredName in SchemaPropertyCollector.CollectRequiredNames(schema, ignoredSchemaReferences))
                {
                    if (inheritedProperties.TryGetValue(requiredName, out var inheritedProperty)
                        && !inheritedProperty.Required
                        && !inheritedProperty.WriteOnly
                        && !redeclaredWriteOnlyNames.Contains(requiredName))
                    {
                        inheritedPropertyRules.Add(new InheritedPropertyRule(requiredName, InheritedPropertyRuleKind.RequireInResponse));
                    }
                }
            }

            var enumKind = _schemaEnumResolver.GetSchemaEnumKind(schema);
            var enumUnderlyingType = enumKind switch
            {
                SchemaEnumKind.Integer => SchemaEnumResolver.GetIntegerEnumUnderlyingType(schema),
                SchemaEnumKind.Number => SchemaEnumResolver.GetNumberEnumValueType(schema),
                _ => null
            };
            var enumMembers = enumKind == SchemaEnumKind.None ? [] : SchemaEnumResolver.CreateEnumMembers(schema, enumKind, declaredTypeName);

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
                enumMembers,
                polymorphicDerivedSchema?.DiscriminatorPropertyName,
                polymorphicDerivedSchema?.SelfAndDescendantTypes,
                inheritedPropertyRules);
        }

        private sealed class InheritedProperty(SchemaPropertyDefinition property, bool readOnly, bool writeOnly, bool required)
        {
            public SchemaPropertyDefinition Property { get; } = property;

            // Effective constraints across the ancestry, which may be stricter than the CLR property's own declaration.
            public bool ReadOnly { get; } = readOnly;
            public bool WriteOnly { get; } = writeOnly;
            public bool Required { get; } = required;
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
            // Inline enums and objects get generated models so object parameters can be validated and expanded.
            RegisterOperationInlineSchema(parameter.Schema, operationName, parameter.Name ?? "Parameter");
        }

        private void RegisterRequestBodyInlineSchema(IOpenApiRequestBody? requestBody, string operationName)
        {
            if (requestBody is null || !TrySelectRequestBodyContent(requestBody, out var selectedContent))
            {
                return;
            }

            RegisterOperationInlineSchema(selectedContent.Value.Schema, operationName, "Body");
        }

        private void RegisterResponseInlineSchema(OpenApiOperation operation, string operationName)
        {
            if (!TrySelectSuccessResponseContent(operation, out _, out var selectedContent))
            {
                return;
            }

            RegisterOperationInlineSchema(selectedContent.Value.Schema, operationName, "Response");
        }

        private void RegisterErrorResponseInlineSchemas(OpenApiOperation operation, string operationName)
        {
            foreach (var (statusPattern, _, selectedContent) in EnumerateErrorResponseContents(operation))
            {
                RegisterOperationInlineSchema(selectedContent.Value.Schema, operationName, BuildErrorResponseChildName(statusPattern));
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
            // Children of component schemas, including aliases, are registered once from the components pass.
            if (schema is null || SchemaReferenceResolver.TryResolveSchemaReferenceId(schema) is not null)
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
                schema,
                GetCompanionTypeSuffix(schema));
        }

        // String and number enums, and dictionaries with properties, are emitted with a sibling `{Name}JsonConverter`
        // class whose name must not collide with other types or members. Every dictionary reserves one, since its
        // properties are not known yet.
        private string? GetCompanionTypeSuffix(IOpenApiSchema schema)
            => _schemaEnumResolver.GetSchemaEnumKind(schema) is SchemaEnumKind.String or SchemaEnumKind.Number
                || SchemaTypeResolver.IsDictionarySchema(schema)
                ? "JsonConverter"
                : null;

        private bool CanGenerateInlineSchema(IOpenApiSchema schema)
        {
            if (SchemaReferenceResolver.TryResolveSchemaReferenceId(schema) is not null
                || SchemaTypeResolver.IsDictionarySchema(schema)
                || schema.OneOf is { Count: > 0 }
                || schema.AnyOf is { Count: > 0 })
            {
                return false;
            }

            // Scalar compositions such as `allOf: [{ type: string, format: uuid }]` keep their underlying CLR type.
            return _schemaEnumResolver.IsEnumSchema(schema)
                || ComposesObject(schema, new HashSet<string>(StringComparer.Ordinal));
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

        private sealed class PolymorphicDerivedSchemaInfo(string baseSchemaName, string baseTypeName, string rootTypeName, string discriminatorPropertyName)
        {
            // The schema and type this subtype directly extends; RootTypeName is the type that declares the discriminator.
            public string BaseSchemaName { get; } = baseSchemaName;
            public string BaseTypeName { get; } = baseTypeName;
            public string RootTypeName { get; } = rootTypeName;
            public string DiscriminatorPropertyName { get; } = discriminatorPropertyName;
            public List<SchemaDerivedTypeDefinition> SelfAndDescendantTypes { get; } = [];
        }

    }
}
