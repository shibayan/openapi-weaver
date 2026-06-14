using Microsoft.OpenApi;

namespace OpenApiWeaver;

public sealed partial class ClientGenerator
{
    private sealed partial class Transformer
    {
        private readonly string _rootNamespace;
        private readonly OpenApiDocument _document;
        private readonly string _clientName;
        private readonly SchemaCatalog _schemaCatalog = new();
        private readonly SchemaAnalysisCache _schemaAnalysisCache = new();
        private readonly SchemaTypeResolver _schemaTypeResolver;
        private readonly Dictionary<string, PolymorphicSchemaInfo> _polymorphicSchemasByTypeName = new(StringComparer.Ordinal);
        private readonly Dictionary<string, PolymorphicDerivedSchemaInfo> _polymorphicDerivedSchemasByTypeName = new(StringComparer.Ordinal);
        private readonly Dictionary<string, SchemaDefinition> _schemaDefinitionsByTypeName = new(StringComparer.Ordinal);

        public Transformer(string documentPath, string rootNamespace, string? clientNameOverride, OpenApiDocument document)
        {
            _rootNamespace = rootNamespace;
            _document = document;
            _clientName = BuildClientName(documentPath, document, clientNameOverride);
            _schemaTypeResolver = new SchemaTypeResolver(document, _schemaCatalog, _schemaAnalysisCache);
        }

        public ClientModel Transform()
        {
            RegisterSchemaNames();
            RegisterPolymorphicSchemaInfo();
            RegisterInlineSchemaNames();

            var securitySchemes = BuildSecuritySchemes();
            var schemas = BuildSchemaDefinitions();
            var serializerOptionsTypeName = schemas.Any(static schema => schema.Properties.Any(static property => property.ReadOnly || property.WriteOnly))
                ? _schemaCatalog.AllocateTypeName(parentTypeName: null, _clientName + "JsonSerializerOptions")
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

    }
}
