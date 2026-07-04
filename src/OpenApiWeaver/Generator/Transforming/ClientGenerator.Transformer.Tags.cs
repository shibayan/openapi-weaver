namespace OpenApiWeaver;

public sealed partial class ClientGenerator
{
    private sealed partial class Transformer
    {
        private List<TagGroup> BuildTagGroups(IReadOnlyList<SecuritySchemeBinding> securitySchemes, string serializerOptionsTypeName)
        {
            var securitySchemesByKey = new Dictionary<string, SecuritySchemeBinding>(securitySchemes.Count, StringComparer.Ordinal);
            foreach (var securityScheme in securitySchemes)
            {
                securitySchemesByKey[securityScheme.SchemeKey] = securityScheme;
            }

            var tagDescriptions = new Dictionary<string, string>(StringComparer.Ordinal);
            if (_document.Tags is not null)
            {
                foreach (var tag in _document.Tags)
                {
                    if (!string.IsNullOrWhiteSpace(tag.Name) && !string.IsNullOrWhiteSpace(tag.Description))
                    {
                        tagDescriptions[tag.Name!] = tag.Description!;
                    }
                }
            }

            var accumulators = new Dictionary<string, TagGroupAccumulator>(StringComparer.Ordinal);

            foreach (var path in _document.Paths)
            {
                foreach (var operation in path.Value.Operations ?? [])
                {
                    var tagName = GetTagName(operation.Value);
                    var groupName = string.IsNullOrWhiteSpace(tagName) ? "Default" : tagName!;

                    if (!accumulators.TryGetValue(groupName, out var accumulator))
                    {
                        tagDescriptions.TryGetValue(groupName, out var description);
                        accumulator = new TagGroupAccumulator(groupName, description);
                        accumulators[groupName] = accumulator;
                    }

                    accumulator.Operations.Add(BuildOperation(path.Key, operation.Key.ToString(), path.Value, operation.Value, accumulator.UsedMethodNames, securitySchemesByKey));
                }
            }

            var usedPropertyNames = new HashSet<string>(StringComparer.Ordinal);
            var reservedClassNames = _schemaCatalog.ComponentTypeNames
                .Concat(_schemaCatalog.InlineSchemas.Where(static schema => schema.ParentTypeName is null).Select(static schema => schema.DeclaredTypeName))
                .Append(_clientName)
                .Concat(SupportTypeNames.ReservedTypeNames);
            if (!string.IsNullOrWhiteSpace(serializerOptionsTypeName))
            {
                reservedClassNames = reservedClassNames.Append(serializerOptionsTypeName);
            }

            var usedClassNames = new HashSet<string>(
                reservedClassNames,
                StringComparer.Ordinal);

            return [..
                accumulators.OrderBy(static pair => pair.Key, StringComparer.Ordinal)
                    .Select(pair =>
                    {
                        var propertyName = AllocateUniqueName(
                            usedPropertyNames,
                            NormalizePascalIdentifier(pair.Value.GroupName, "Default"),
                            "Default");
                        var className = AllocateUniqueName(
                            usedClassNames,
                            propertyName.EndsWith("Client", StringComparison.Ordinal) ? propertyName : propertyName + "Client",
                            "DefaultClient");
                        return new TagGroup(propertyName, className, pair.Value.Description, pair.Value.Operations);
                    })];
        }

        private sealed class TagGroupAccumulator(string groupName, string? description)
        {
            public string GroupName { get; } = groupName;
            public string? Description { get; } = description;
            public List<OperationGroupItem> Operations { get; } = [];
            public HashSet<string> UsedMethodNames { get; } = new(StringComparer.Ordinal);
        }
    }
}
