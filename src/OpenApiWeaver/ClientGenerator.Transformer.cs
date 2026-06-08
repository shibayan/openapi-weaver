using Microsoft.OpenApi;

namespace OpenApiWeaver;

public sealed partial class ClientGenerator
{
    private sealed partial class Transformer
    {
        private readonly string _rootNamespace;
        private readonly OpenApiDocument _document;
        private readonly string _clientName;
        private readonly Dictionary<string, string> _schemaNames = new(StringComparer.Ordinal);
        private readonly Dictionary<string, InlineSchemaInfo> _inlineSchemasByIdentity = new(StringComparer.Ordinal);
        private readonly Dictionary<string, PolymorphicSchemaInfo> _polymorphicSchemasByTypeName = new(StringComparer.Ordinal);
        private readonly Dictionary<string, PolymorphicDerivedSchemaInfo> _polymorphicDerivedSchemasByTypeName = new(StringComparer.Ordinal);
        private readonly Dictionary<string, SchemaDefinition> _schemaDefinitionsByTypeName = new(StringComparer.Ordinal);
        private readonly Dictionary<IOpenApiSchema, TypeUsage> _requiredTypeUsagesBySchema = new(ReferenceEqualityComparer<IOpenApiSchema>.Instance);
        private readonly Dictionary<IOpenApiSchema, TypeUsage> _optionalTypeUsagesBySchema = new(ReferenceEqualityComparer<IOpenApiSchema>.Instance);
        private readonly Dictionary<IOpenApiSchema, string?> _dictionaryValueTypesBySchema = new(ReferenceEqualityComparer<IOpenApiSchema>.Instance);
        private readonly Dictionary<IOpenApiSchema, TypeShape> _typeShapesBySchema = new(ReferenceEqualityComparer<IOpenApiSchema>.Instance);
        private readonly Dictionary<IOpenApiSchema, bool> _schemaNullabilityBySchema = new(ReferenceEqualityComparer<IOpenApiSchema>.Instance);
        private readonly Dictionary<IOpenApiSchema, SchemaEnumKind> _schemaEnumKindsBySchema = new(ReferenceEqualityComparer<IOpenApiSchema>.Instance);
        private readonly HashSet<string> _usedSchemaTypeNames = new(StringComparer.Ordinal);
        private readonly List<InlineSchemaInfo> _inlineSchemas = [];

        public Transformer(string documentPath, string rootNamespace, string? clientNameOverride, OpenApiDocument document)
        {
            _rootNamespace = rootNamespace;
            _document = document;
            _clientName = BuildClientName(documentPath, document, clientNameOverride);
        }

        public ClientModel Transform()
        {
            RegisterSchemaNames();
            RegisterPolymorphicSchemaInfo();
            RegisterInlineSchemaNames();

            var securitySchemes = BuildSecuritySchemes();
            var schemas = BuildSchemaDefinitions();
            var serializerOptionsTypeName = schemas.Any(static schema => schema.Properties.Any(static property => property.ReadOnly || property.WriteOnly))
                ? AllocateTypeName(parentTypeName: null, _clientName + "JsonSerializerOptions")
                : string.Empty;
            var tagGroups = BuildTagGroups(securitySchemes, serializerOptionsTypeName);

            return new ClientModel(
                _rootNamespace,
                _clientName,
                serializerOptionsTypeName,
                !string.IsNullOrWhiteSpace(_document.Info.Title) ? _document.Info.Title! : _clientName,
                _document.Info.Description,
                _document.Servers?.FirstOrDefault()?.Url,
                schemas,
                tagGroups,
                securitySchemes);
        }

        private sealed class ReferenceEqualityComparer<T> : IEqualityComparer<T>
            where T : class
        {
            public static ReferenceEqualityComparer<T> Instance { get; } = new();

            private ReferenceEqualityComparer()
            {
            }

            public bool Equals(T? x, T? y)
                => ReferenceEquals(x, y);

            public int GetHashCode(T obj)
                => System.Runtime.CompilerServices.RuntimeHelpers.GetHashCode(obj);
        }
    }
}
