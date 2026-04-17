using Microsoft.OpenApi;

namespace OpenApiWeaver;

public sealed partial class ClientGenerator
{
    private sealed partial class DocumentTransformer
    {
        private readonly string _rootNamespace;
        private readonly OpenApiDocument _document;
        private readonly string _clientName;
        private readonly Dictionary<string, string> _schemaNames = new(StringComparer.Ordinal);
        private readonly Dictionary<string, InlineSchemaInfo> _inlineSchemasByIdentity = new(StringComparer.Ordinal);
        private readonly HashSet<string> _usedSchemaTypeNames = new(StringComparer.Ordinal);
        private readonly List<InlineSchemaInfo> _inlineSchemas = [];
        private readonly Dictionary<string, string> _tagDescriptions = new(StringComparer.Ordinal);

        public DocumentTransformer(string documentPath, string rootNamespace, string? clientNameOverride, OpenApiDocument document)
        {
            _rootNamespace = rootNamespace;
            _document = document;
            _clientName = BuildClientName(documentPath, document, clientNameOverride);
        }

        public ClientModel Transform()
        {
            RegisterSchemaNames();
            RegisterInlineSchemaNames();

            var schemas = BuildSchemaDefinitions();
            var tagGroups = BuildTagGroups();
            var securitySchemes = BuildSecuritySchemes();

            return new ClientModel(
                _rootNamespace,
                _clientName,
                !string.IsNullOrWhiteSpace(_document.Info.Title) ? _document.Info.Title! : _clientName,
                _document.Info.Description,
                _document.Servers?.FirstOrDefault()?.Url,
                schemas,
                tagGroups,
                securitySchemes);
        }
    }
}
