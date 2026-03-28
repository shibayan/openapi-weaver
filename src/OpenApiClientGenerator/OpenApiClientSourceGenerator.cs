
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;

using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Text;
using Microsoft.OpenApi;
using Microsoft.OpenApi.Reader;
using Microsoft.OpenApi.YamlReader;

namespace OpenApiClientGenerator;

[Generator]
public sealed class OpenApiClientSourceGenerator : IIncrementalGenerator
{
    public void Initialize(IncrementalGeneratorInitializationContext context)
    {
        var rootNamespace = context.AnalyzerConfigOptionsProvider
            .Select(static (provider, _) =>
            {
                provider.GlobalOptions.TryGetValue("build_property.RootNamespace", out var value);
                return value ?? string.Empty;
            });

        var openApiFiles = context.AdditionalTextsProvider
            .Where(static file => IsOpenApiDocument(file.Path));

        context.RegisterSourceOutput(openApiFiles.Combine(rootNamespace), static (productionContext, pair) =>
        {
            var file = pair.Left;
            var configuredRootNamespace = pair.Right;
            var sourceText = file.GetText(productionContext.CancellationToken);
            var content = sourceText?.ToString();
            if (string.IsNullOrWhiteSpace(content))
            {
                productionContext.ReportDiagnostic(Diagnostic.Create(Diagnostics.DocumentEmpty, Location.None, file.Path));
                return;
            }

            ReadResult readResult;
            try
            {
                using var stream = new MemoryStream();
                using (var writer = new StreamWriter(stream, Encoding.UTF8, bufferSize: 1024, leaveOpen: true))
                {
                    sourceText!.Write(writer);
                }
                stream.Position = 0;
                readResult = ReadOpenApiDocument(file.Path, stream);
            }
            catch (Exception exception)
            {
                productionContext.ReportDiagnostic(Diagnostic.Create(
                    Diagnostics.DocumentInvalid,
                    Location.None,
                    file.Path,
                    exception.Message));
                return;
            }

            var document = readResult.Document;
            var diagnostic = readResult.Diagnostic;
            if (document?.Paths is null)
            {
                productionContext.ReportDiagnostic(Diagnostic.Create(
                    Diagnostics.DocumentInvalid,
                    Location.None,
                    file.Path,
                    "The document could not be loaded."));
                return;
            }

            if (diagnostic?.Errors.Count > 0)
            {
                productionContext.ReportDiagnostic(Diagnostic.Create(
                    Diagnostics.DocumentHasWarnings,
                    Location.None,
                    file.Path,
                    string.Join(", ", diagnostic.Errors.Select(static error => error.Message))));
            }

            var source = new ClientEmitter(file.Path, configuredRootNamespace, document).Emit();
            productionContext.AddSource($"{SanitizeHintName(file.Path)}.g.cs", SourceText.From(source, Encoding.UTF8));
        });
    }

    private static bool IsOpenApiDocument(string path)
    {
        var extension = Path.GetExtension(path);
        return string.Equals(extension, ".yaml", StringComparison.OrdinalIgnoreCase)
            || string.Equals(extension, ".yml", StringComparison.OrdinalIgnoreCase)
            || string.Equals(extension, ".json", StringComparison.OrdinalIgnoreCase);
    }

    private static ReadResult ReadOpenApiDocument(string path, MemoryStream stream)
    {
        var location = CreateDocumentUri(path);
        var settings = new OpenApiReaderSettings();

        if (IsYamlDocument(path))
        {
            settings.AddYamlReader();
            return new OpenApiYamlReader().Read(stream, location, settings);
        }

        settings.AddJsonReader();
        return new OpenApiJsonReader().Read(stream, location, settings);
    }

    private static bool IsYamlDocument(string path)
    {
        var extension = Path.GetExtension(path);
        return string.Equals(extension, ".yaml", StringComparison.OrdinalIgnoreCase)
            || string.Equals(extension, ".yml", StringComparison.OrdinalIgnoreCase);
    }

    private static Uri CreateDocumentUri(string path)
    {
        return Uri.TryCreate(path, UriKind.Absolute, out var absoluteUri)
            ? absoluteUri
            : new Uri(Path.GetFullPath(path));
    }

    private static string SanitizeHintName(string value)
        => value.Replace('<', '_').Replace('>', '_').Replace('.', '_').Replace('\\', '_').Replace('/', '_').Replace(':', '_');

    private static class Diagnostics
    {
        public static readonly DiagnosticDescriptor DocumentEmpty = new(
            "OARSG002",
            "OpenAPI document is empty",
            "The OpenAPI document '{0}' is empty",
            "OpenApiClientGenerator",
            DiagnosticSeverity.Error,
            isEnabledByDefault: true);

        public static readonly DiagnosticDescriptor DocumentHasWarnings = new(
            "OARSG003",
            "OpenAPI document has validation warnings",
            "The OpenAPI document '{0}' was loaded with validation warnings: {1}",
            "OpenApiClientGenerator",
            DiagnosticSeverity.Warning,
            isEnabledByDefault: true);

        public static readonly DiagnosticDescriptor DocumentInvalid = new(
            "OARSG004",
            "OpenAPI document is invalid",
            "The OpenAPI document '{0}' could not be parsed: {1}",
            "OpenApiClientGenerator",
            DiagnosticSeverity.Error,
            isEnabledByDefault: true);
    }

    private sealed class ClientEmitter(string documentPath, string rootNamespace, OpenApiDocument document)
    {
        private static readonly HashSet<string> s_reservedIdentifiers = new(StringComparer.Ordinal)
        {
            "abstract", "as", "base", "bool", "break", "byte", "case", "catch", "char", "checked", "class",
            "const", "continue", "decimal", "default", "delegate", "do", "double", "else", "enum", "event",
            "explicit", "extern", "false", "finally", "fixed", "float", "for", "foreach", "goto", "if",
            "implicit", "in", "int", "interface", "internal", "is", "lock", "long", "namespace", "new",
            "null", "object", "operator", "out", "override", "params", "private", "protected", "public",
            "readonly", "ref", "return", "sbyte", "sealed", "short", "sizeof", "stackalloc", "static",
            "string", "struct", "switch", "this", "throw", "true", "try", "typeof", "uint", "ulong",
            "unchecked", "unsafe", "ushort", "using", "virtual", "void", "volatile", "while"
        };

        private readonly Dictionary<string, string> _schemaNames = new(StringComparer.Ordinal);
        private readonly HashSet<string> _enumSchemaIds = new(StringComparer.Ordinal);
        private readonly string _clientName = BuildClientName(documentPath, document);
        private readonly List<TagGroup> _tagGroups = [];
        private readonly List<SecuritySchemeBinding> _securitySchemes = [];
        private List<SecuritySchemeBinding> _querySecuritySchemes = [];

        private bool _needsFormUrlEncoded;
        private bool _needsMultipartFormData;
        private bool _needsFormatParameter;

        public string Emit()
        {
            RegisterSchemaNames();
            RegisterTagGroups();
            RegisterSecuritySchemes();
            _querySecuritySchemes = _securitySchemes.Where(static scheme => scheme.Location == SecuritySchemeLocation.Query).ToList();
            AnalyzeRequiredHelpers();

            var builder = new StringBuilder();
            builder.AppendLine("// <auto-generated />");
            builder.AppendLine("#nullable enable");
            builder.AppendLine("using System;");

            if (_needsFormUrlEncoded || _needsMultipartFormData)
            {
                builder.AppendLine("using System.Collections;");
            }

            builder.AppendLine("using System.Collections.Generic;");

            if (_needsFormatParameter)
            {
                builder.AppendLine("using System.Globalization;");
            }

            builder.AppendLine("using System.Net.Http;");
            builder.AppendLine("using System.Net.Http.Json;");
            builder.AppendLine("using System.Text.Json;");
            builder.AppendLine("using System.Text.Json.Serialization;");
            builder.AppendLine("using System.Threading;");
            builder.AppendLine("using System.Threading.Tasks;");
            builder.AppendLine();

            if (!string.IsNullOrWhiteSpace(rootNamespace))
            {
                builder.Append("namespace ").Append(rootNamespace).AppendLine(";");
                builder.AppendLine();
            }

            EmitSchemas(builder);

            if (_tagGroups.Count > 0)
            {
                EmitHelperClass(builder);
                builder.AppendLine();
            }

            EmitTagClients(builder);
            EmitClient(builder);
            return builder.ToString();
        }

        private void AnalyzeRequiredHelpers()
        {
            foreach (var tagGroup in _tagGroups)
            {
                foreach (var operation in tagGroup.Operations)
                {
                    var requestBody = ResolveRequestBody(operation.Operation.RequestBody);
                    if (requestBody is not null)
                    {
                        switch (requestBody.Kind)
                        {
                            case RequestBodyKind.FormUrlEncoded:
                                _needsFormUrlEncoded = true;
                                _needsFormatParameter = true;
                                break;
                            case RequestBodyKind.MultipartFormData:
                                _needsMultipartFormData = true;
                                _needsFormatParameter = true;
                                break;
                        }
                    }

                    var parameters = (operation.PathItem.Parameters ?? []).Concat(operation.Operation.Parameters ?? []).ToList();
                    if (parameters.Count > 0)
                    {
                        _needsFormatParameter = true;
                    }
                }
            }

            if (_querySecuritySchemes.Count > 0)
            {
                _needsFormatParameter = true;
            }
        }

        private void RegisterSchemaNames()
        {
            if (document.Components?.Schemas is null)
            {
                return;
            }

            foreach (var schema in document.Components.Schemas)
            {
                _schemaNames[schema.Key] = SafeIdentifier(ToPascalCase(schema.Key));
                if (IsEnumSchema(schema.Value))
                {
                    _enumSchemaIds.Add(schema.Key);
                }
            }
        }

        private void RegisterTagGroups()
        {
            var groups = new Dictionary<string, TagGroup>(StringComparer.Ordinal);

            foreach (var path in document.Paths)
            {
                foreach (var operation in path.Value.Operations ?? [])
                {
                    var tagName = GetTagName(operation.Value);
                    var groupName = string.IsNullOrWhiteSpace(tagName) ? "Default" : tagName!;
                    var propertyName = SafeIdentifier(ToPascalCase(groupName));
                    var className = propertyName.EndsWith("Client", StringComparison.Ordinal) ? propertyName : propertyName + "Client";

                    if (!groups.TryGetValue(propertyName, out var group))
                    {
                        group = new TagGroup(propertyName, className);
                        groups.Add(propertyName, group);
                    }

                    group.Operations.Add(new OperationGroupItem(path.Key, path.Value, operation.Key.ToString(), operation.Value));
                }
            }

            _tagGroups.AddRange(groups.Values.OrderBy(static group => group.PropertyName, StringComparer.Ordinal));
        }

        private void RegisterSecuritySchemes()
        {
            if (document.Components?.SecuritySchemes is null)
            {
                return;
            }

            foreach (var scheme in document.Components.SecuritySchemes)
            {
                var binding = CreateSecuritySchemeBinding(scheme.Key, scheme.Value);
                if (binding is not null)
                {
                    _securitySchemes.Add(binding);
                }
            }
        }

        private void EmitSchemas(StringBuilder builder)
        {
            if (document.Components?.Schemas is null)
            {
                return;
            }

            foreach (var schema in document.Components.Schemas)
            {
                if (IsEnumSchema(schema.Value))
                {
                    EmitEnumSchema(builder, _schemaNames[schema.Key], schema.Value);
                }
                else
                {
                    EmitSchema(builder, _schemaNames[schema.Key], schema.Value);
                }
                builder.AppendLine();
            }
        }

        private void EmitSchema(StringBuilder builder, string typeName, IOpenApiSchema schema)
        {
            builder.Append("public sealed class ").Append(typeName).AppendLine();
            builder.AppendLine("{");

            if (schema.Properties is null)
            {
                builder.AppendLine("}");
                return;
            }

            foreach (var property in schema.Properties)
            {
                var propertyName = SafeIdentifier(ToPascalCase(property.Key));
                var required = schema.Required?.Contains(property.Key) == true;
                var propertyType = ResolveTypeName(property.Value, required);
                var requiredModifier = required ? "required " : string.Empty;
                builder.Append("    [JsonPropertyName(\"").Append(EscapeStringLiteral(property.Key)).AppendLine("\")]");
                builder.Append("    public ").Append(requiredModifier).Append(propertyType).Append(' ').Append(propertyName).AppendLine(" { get; init; }");
            }

            builder.AppendLine("}");
        }

        private static bool IsEnumSchema(IOpenApiSchema schema)
        {
            return schema.Enum is { Count: > 0 } && HasSchemaType(schema, JsonSchemaType.String);
        }

        private static void EmitEnumSchema(StringBuilder builder, string typeName, IOpenApiSchema schema)
        {
            builder.Append("[JsonConverter(typeof(").Append(typeName).AppendLine("JsonConverter))]");
            builder.Append("public readonly record struct ").Append(typeName).AppendLine("(string Value)");
            builder.AppendLine("{");

            foreach (var item in schema.Enum ?? [])
            {
                var enumValue = item?.ToString();
                if (string.IsNullOrWhiteSpace(enumValue))
                {
                    continue;
                }

                var memberName = SafeIdentifier(ToPascalCase(enumValue ?? string.Empty));
                builder.Append("    public static readonly ").Append(typeName).Append(' ').Append(memberName).Append(" = new(\"").Append(EscapeStringLiteral(enumValue ?? string.Empty)).AppendLine("\");");
            }

            builder.AppendLine();
            builder.AppendLine("    public override string ToString() => Value;");
            builder.AppendLine("}");

            builder.AppendLine();

            builder.Append("public sealed class ").Append(typeName).Append("JsonConverter : JsonConverter<").Append(typeName).AppendLine(">");
            builder.AppendLine("{");
            builder.Append("    public override ").Append(typeName).AppendLine(" Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)");
            builder.AppendLine("    {");
            builder.Append("        return new ").Append(typeName).AppendLine("(reader.GetString()!);");
            builder.AppendLine("    }");
            builder.AppendLine();
            builder.Append("    public override void Write(Utf8JsonWriter writer, ").Append(typeName).AppendLine(" value, JsonSerializerOptions options)");
            builder.AppendLine("    {");
            builder.AppendLine("        writer.WriteStringValue(value.Value);");
            builder.AppendLine("    }");
            builder.AppendLine("}");
        }

        private void EmitHelperClass(StringBuilder builder)
        {
            builder.AppendLine("internal static class OpenApiClientHelpers");
            builder.AppendLine("{");
            builder.AppendLine("    internal static readonly JsonSerializerOptions SerializerOptions = new(JsonSerializerDefaults.Web);");
            builder.AppendLine();
            builder.AppendLine("    internal static string GetSerializedPropertyName(System.Reflection.PropertyInfo property)");
            builder.AppendLine("    {");
            builder.AppendLine("        var attribute = (JsonPropertyNameAttribute?)Attribute.GetCustomAttribute(property, typeof(JsonPropertyNameAttribute));");
            builder.AppendLine("        if (attribute?.Name is { Length: > 0 } name)");
            builder.AppendLine("        {");
            builder.AppendLine("            return name;");
            builder.AppendLine("        }");
            builder.AppendLine();
            builder.AppendLine("        if (property.Name.Length == 1)");
            builder.AppendLine("        {");
            builder.AppendLine("            return char.ToLowerInvariant(property.Name[0]).ToString();");
            builder.AppendLine("        }");
            builder.AppendLine();
            builder.AppendLine("        return char.ToLowerInvariant(property.Name[0]) + property.Name[1..];");
            builder.AppendLine("    }");

            if (_needsFormatParameter)
            {
                builder.AppendLine();
                builder.AppendLine("    internal static string FormatParameter(object? value)");
                builder.AppendLine("    {");
                builder.AppendLine("        return value switch");
                builder.AppendLine("        {");
                builder.AppendLine("            null => string.Empty,");
                builder.AppendLine("            JsonElement element => element.ToString(),");
                builder.AppendLine("            IFormattable formattable => formattable.ToString(null, CultureInfo.InvariantCulture),");
                builder.AppendLine("            _ => value.ToString() ?? string.Empty");
                builder.AppendLine("        };");
                builder.AppendLine("    }");
            }

            if (_needsFormUrlEncoded)
            {
                builder.AppendLine();
                builder.AppendLine("    internal static FormUrlEncodedContent CreateFormUrlEncodedContent<TBody>(TBody body)");
                builder.AppendLine("    {");
                builder.AppendLine("        var values = new List<KeyValuePair<string, string>>();");
                builder.AppendLine();
                builder.AppendLine("        foreach (var property in typeof(TBody).GetProperties(System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.Public))");
                builder.AppendLine("        {");
                builder.AppendLine("            var value = property.GetValue(body);");
                builder.AppendLine("            if (value is null)");
                builder.AppendLine("            {");
                builder.AppendLine("                continue;");
                builder.AppendLine("            }");
                builder.AppendLine();
                builder.AppendLine("            var name = GetSerializedPropertyName(property);");
                builder.AppendLine("            AddFormValues(values, name, value);");
                builder.AppendLine("        }");
                builder.AppendLine();
                builder.AppendLine("        return new FormUrlEncodedContent(values);");
                builder.AppendLine("    }");
                builder.AppendLine();
                builder.AppendLine("    private static void AddFormValues(List<KeyValuePair<string, string>> values, string name, object value)");
                builder.AppendLine("    {");
                builder.AppendLine("        if (value is string or byte[])");
                builder.AppendLine("        {");
                builder.AppendLine("            values.Add(new KeyValuePair<string, string>(name, FormatParameter(value)));");
                builder.AppendLine("            return;");
                builder.AppendLine("        }");
                builder.AppendLine();
                builder.AppendLine("        if (value is IEnumerable enumerable)");
                builder.AppendLine("        {");
                builder.AppendLine("            foreach (var item in enumerable)");
                builder.AppendLine("            {");
                builder.AppendLine("                if (item is not null)");
                builder.AppendLine("                {");
                builder.AppendLine("                    AddFormValues(values, name, item);");
                builder.AppendLine("                }");
                builder.AppendLine("            }");
                builder.AppendLine();
                builder.AppendLine("            return;");
                builder.AppendLine("        }");
                builder.AppendLine();
                builder.AppendLine("        values.Add(new KeyValuePair<string, string>(name, FormatParameter(value)));");
                builder.AppendLine("    }");
            }

            if (_needsMultipartFormData)
            {
                builder.AppendLine();
                builder.AppendLine("    internal static MultipartFormDataContent CreateMultipartFormDataContent<TBody>(TBody body)");
                builder.AppendLine("    {");
                builder.AppendLine("        var content = new MultipartFormDataContent();");
                builder.AppendLine();
                builder.AppendLine("        foreach (var property in typeof(TBody).GetProperties(System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.Public))");
                builder.AppendLine("        {");
                builder.AppendLine("            var value = property.GetValue(body);");
                builder.AppendLine("            if (value is null)");
                builder.AppendLine("            {");
                builder.AppendLine("                continue;");
                builder.AppendLine("            }");
                builder.AppendLine();
                builder.AppendLine("            var name = GetSerializedPropertyName(property);");
                builder.AppendLine("            AddMultipartContent(content, name, value);");
                builder.AppendLine("        }");
                builder.AppendLine();
                builder.AppendLine("        return content;");
                builder.AppendLine("    }");
                builder.AppendLine();
                builder.AppendLine("    private static void AddMultipartContent(MultipartFormDataContent content, string name, object value)");
                builder.AppendLine("    {");
                builder.AppendLine("        switch (value)");
                builder.AppendLine("        {");
                builder.AppendLine("            case byte[] bytes:");
                builder.AppendLine("                content.Add(new ByteArrayContent(bytes), name, name);");
                builder.AppendLine("                return;");
                builder.AppendLine("            case string text:");
                builder.AppendLine("                content.Add(new StringContent(text), name);");
                builder.AppendLine("                return;");
                builder.AppendLine("            case IEnumerable enumerable:");
                builder.AppendLine("                foreach (var item in enumerable)");
                builder.AppendLine("                {");
                builder.AppendLine("                    if (item is not null)");
                builder.AppendLine("                    {");
                builder.AppendLine("                        AddMultipartContent(content, name, item);");
                builder.AppendLine("                    }");
                builder.AppendLine("                }");
                builder.AppendLine("                return;");
                builder.AppendLine("            default:");
                builder.AppendLine("                content.Add(new StringContent(FormatParameter(value)), name);");
                builder.AppendLine("                return;");
                builder.AppendLine("        }");
                builder.AppendLine("    }");
            }

            builder.AppendLine("}");
        }

        private void EmitTagClients(StringBuilder builder)
        {
            foreach (var tagGroup in _tagGroups)
            {
                EmitTagClient(builder, tagGroup);
                builder.AppendLine();
            }
        }

        private void EmitTagClient(StringBuilder builder, TagGroup tagGroup)
        {
            builder.Append("public sealed class ").Append(tagGroup.ClassName).AppendLine();
            builder.AppendLine("{");
            builder.AppendLine("    private readonly HttpClient _httpClient;");
            foreach (var securityScheme in _querySecuritySchemes)
            {
                builder.Append("    private readonly string? ").Append(securityScheme.FieldName).AppendLine(";");
            }
            builder.AppendLine();
            var constructorParameters = string.Join(", ",
                new[] { "HttpClient httpClient" }.Concat(
                    _querySecuritySchemes
                        .Select(static scheme => $"string? {scheme.ParameterName}")));
            builder.Append("    internal ").Append(tagGroup.ClassName).Append('(').Append(constructorParameters).AppendLine(")");
            builder.AppendLine("    {");
            builder.AppendLine("        _httpClient = httpClient;");
            foreach (var securityScheme in _querySecuritySchemes)
            {
                builder.Append("        ").Append(securityScheme.FieldName).Append(" = ").Append(securityScheme.ParameterName).AppendLine(";");
            }
            builder.AppendLine("    }");

            foreach (var operation in tagGroup.Operations)
            {
                builder.AppendLine();
                EmitOperation(builder, operation.Route, operation.PathItem, operation.OperationType, operation.Operation);
            }

            builder.AppendLine("}");
        }

        private void EmitClient(StringBuilder builder)
        {
            var serverUrl = document.Servers?.FirstOrDefault()?.Url;
            var constructorParameters = string.Join(", ", _securitySchemes.Select(static scheme => scheme.ParameterDeclaration));

            builder.Append("public partial class ").Append(_clientName).AppendLine(" : IDisposable");
            builder.AppendLine("{");
            builder.AppendLine("    private readonly HttpClient _httpClient;");
            foreach (var securityScheme in _querySecuritySchemes)
            {
                builder.Append("    private readonly string? ").Append(securityScheme.FieldName).AppendLine(";");
            }
            builder.AppendLine();
            builder.Append("    public ").Append(_clientName).Append('(').Append(constructorParameters).AppendLine(")");
            builder.AppendLine("    {");
            builder.AppendLine("        _httpClient = new HttpClient();");
            if (Uri.TryCreate(serverUrl, UriKind.Absolute, out _))
            {
                builder.Append("        _httpClient.BaseAddress = new Uri(\"").Append(EscapeStringLiteral(serverUrl!)).AppendLine("\", UriKind.Absolute);");
            }

            foreach (var securityScheme in _securitySchemes)
            {
                EmitSecuritySchemeInitialization(builder, securityScheme);
            }

            foreach (var tagGroup in _tagGroups)
            {
                var constructorArguments = string.Join(", ",
                    new[] { "_httpClient" }.Concat(
                        _querySecuritySchemes.Select(static scheme => scheme.FieldName)));
                builder.Append("        ").Append(tagGroup.PropertyName).Append(" = new ").Append(tagGroup.ClassName).Append('(').Append(constructorArguments).AppendLine(");");
            }

            builder.AppendLine("    }");
            builder.AppendLine();

            foreach (var tagGroup in _tagGroups)
            {
                builder.Append("    public ").Append(tagGroup.ClassName).Append(' ').Append(tagGroup.PropertyName).AppendLine(" { get; }");
            }

            if (_tagGroups.Count > 0)
            {
                builder.AppendLine();
            }

            builder.AppendLine("    public void Dispose()");
            builder.AppendLine("    {");
            builder.AppendLine("        _httpClient.Dispose();");
            builder.AppendLine("    }");

            builder.AppendLine("}");
        }

        private void EmitOperation(StringBuilder builder, string route, IOpenApiPathItem pathItem, string operationType, OpenApiOperation operation)
        {
            var tagName = GetTagName(operation);
            var methodName = BuildOperationMethodName(operation.OperationId, operationType, route, tagName);
            var parameters = (pathItem.Parameters ?? []).Concat(operation.Parameters ?? []).ToList();
            var requestBody = ResolveRequestBody(operation.RequestBody);
            var response = ResolveResponse(operation.Responses ?? []);

            var requiredMethodParameters = new List<string>();
            var optionalMethodParameters = new List<string>();

            foreach (var parameter in parameters)
            {
                var parameterName = SafeIdentifier(ToCamelCase(parameter.Name ?? string.Empty));
                var parameterDeclaration = $"{ResolveTypeName(parameter.Schema, parameter.Required)} {parameterName}";
                if (parameter.Required)
                {
                    requiredMethodParameters.Add(parameterDeclaration);
                }
                else
                {
                    optionalMethodParameters.Add($"{parameterDeclaration} = default");
                }
            }

            if (requestBody is not null)
            {
                var bodyTypeName = requestBody.IsRequired ? requestBody.TypeName : MakeNullableTypeName(requestBody.TypeName);
                var bodyParameter = $"{bodyTypeName} body";
                if (requestBody.IsRequired)
                {
                    requiredMethodParameters.Add(bodyParameter);
                }
                else
                {
                    optionalMethodParameters.Add($"{bodyParameter} = default");
                }
            }

            var methodParameters = requiredMethodParameters
                .Concat(optionalMethodParameters)
                .Append("CancellationToken cancellationToken = default")
                .ToList();

            if (response.Kind == ResponseKind.None)
            {
                builder.Append("    public async Task ").Append(methodName).Append("Async(").Append(string.Join(", ", methodParameters)).AppendLine(")");
            }
            else
            {
                builder.Append("    public async Task<").Append(response.TypeName).Append("> ").Append(methodName).Append("Async(").Append(string.Join(", ", methodParameters)).AppendLine(")");
            }
            builder.AppendLine("    {");
            builder.Append("        var path = \"").Append(route).AppendLine("\";");

            foreach (var parameter in parameters.Where(static parameter => parameter.In == ParameterLocation.Path))
            {
                var parameterName = SafeIdentifier(ToCamelCase(parameter.Name ?? string.Empty));
                builder.Append("        path = path.Replace(\"{").Append(parameter.Name).Append("}\", Uri.EscapeDataString(OpenApiClientHelpers.FormatParameter(").Append(parameterName).AppendLine(")));");
            }

            var queryParameters = parameters.Where(static parameter => parameter.In == ParameterLocation.Query).ToList();
            if (queryParameters.Count > 0)
            {
                builder.AppendLine("        var query = new List<string>();");
                foreach (var parameter in queryParameters)
                {
                    var parameterName = SafeIdentifier(ToCamelCase(parameter.Name ?? string.Empty));
                    if (parameter.Required)
                    {
                        builder.Append("        query.Add(\"").Append(parameter.Name).Append("=\" + Uri.EscapeDataString(OpenApiClientHelpers.FormatParameter(").Append(parameterName).AppendLine(")));");
                    }
                    else
                    {
                        builder.Append("        if (").Append(parameterName).AppendLine(" is not null)");
                        builder.AppendLine("        {");
                        builder.Append("            query.Add(\"").Append(parameter.Name).Append("=\" + Uri.EscapeDataString(OpenApiClientHelpers.FormatParameter(").Append(parameterName).AppendLine(")));");
                        builder.AppendLine("        }");
                    }
                }

                builder.AppendLine("        if (query.Count > 0)");
                builder.AppendLine("        {");
                builder.AppendLine("            path += \"?\" + string.Join(\"&\", query);");
                builder.AppendLine("        }");
            }

            foreach (var securityScheme in _querySecuritySchemes)
            {
                builder.Append("        if (").Append(securityScheme.FieldName).AppendLine(" is not null)");
                builder.AppendLine("        {");
                builder.Append("            path += path.Contains(\"?\", StringComparison.Ordinal) ? \"&\" : \"?\";").AppendLine();
                builder.Append("            path += \"").Append(securityScheme.HeaderOrParameterName).Append("=\" + Uri.EscapeDataString(").Append(securityScheme.FieldName).AppendLine(");");
                builder.AppendLine("        }");
            }

            builder.Append("        using var request = new HttpRequestMessage(HttpMethod.").Append(ToPascalCase(operationType.ToLowerInvariant())).AppendLine(", new Uri(path, UriKind.Relative));");

            foreach (var parameter in parameters.Where(static parameter => parameter.In == ParameterLocation.Header))
            {
                var parameterName = SafeIdentifier(ToCamelCase(parameter.Name ?? string.Empty));
                builder.Append("        if (").Append(parameterName).AppendLine(" is not null)");
                builder.AppendLine("        {");
                builder.Append("            request.Headers.TryAddWithoutValidation(\"").Append(parameter.Name).Append("\", OpenApiClientHelpers.FormatParameter(").Append(parameterName).AppendLine("));");
                builder.AppendLine("        }");
            }

            if (requestBody is not null)
            {
                if (requestBody.IsRequired)
                {
                    EmitRequestBodyContentAssignment(builder, requestBody, nullableBody: false);
                }
                else
                {
                    builder.AppendLine("        if (body is not null)");
                    builder.AppendLine("        {");
                    EmitRequestBodyContentAssignment(builder, requestBody, nullableBody: true);
                    builder.AppendLine("        }");
                }
            }

            builder.AppendLine("        using var response = await _httpClient.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken).ConfigureAwait(false);");
            builder.AppendLine("        response.EnsureSuccessStatusCode();");

            if (response.Kind == ResponseKind.None)
            {
                builder.AppendLine("        return;");
            }
            else if (response.Kind == ResponseKind.String)
            {
                builder.AppendLine("        return await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);");
            }
            else if (response.Kind == ResponseKind.Binary)
            {
                builder.AppendLine("        return await response.Content.ReadAsByteArrayAsync(cancellationToken).ConfigureAwait(false);");
            }
            else if (RequiresNonNullJsonResponse(response.TypeName))
            {
                builder.Append("        return await response.Content.ReadFromJsonAsync<").Append(response.TypeName).AppendLine(">(OpenApiClientHelpers.SerializerOptions, cancellationToken).ConfigureAwait(false)");
                builder.AppendLine("            ?? throw new InvalidOperationException(\"The response body was empty.\");");
            }
            else
            {
                builder.Append("        return await response.Content.ReadFromJsonAsync<").Append(response.TypeName).AppendLine(">(OpenApiClientHelpers.SerializerOptions, cancellationToken).ConfigureAwait(false);");
            }

            builder.AppendLine("    }");
        }

        private static void EmitRequestBodyContentAssignment(StringBuilder builder, RequestBodyInfo requestBody, bool nullableBody)
        {
            var bodyValue = nullableBody ? "body!" : "body";

            switch (requestBody.Kind)
            {
                case RequestBodyKind.FormUrlEncoded:
                    builder.Append("            request.Content = OpenApiClientHelpers.CreateFormUrlEncodedContent(").Append(bodyValue).AppendLine(");");
                    break;
                case RequestBodyKind.MultipartFormData:
                    builder.Append("            request.Content = OpenApiClientHelpers.CreateMultipartFormDataContent(").Append(bodyValue).AppendLine(");");
                    break;
                default:
                    builder.Append("            request.Content = JsonContent.Create(").Append(bodyValue).AppendLine(", options: OpenApiClientHelpers.SerializerOptions);");
                    break;
            }
        }

        private RequestBodyInfo? ResolveRequestBody(IOpenApiRequestBody? requestBody)
        {
            if (requestBody?.Content is null || requestBody.Content.Count == 0)
            {
                return null;
            }

            var selectedContent = requestBody.Content.FirstOrDefault(static item => string.Equals(item.Key, "application/json", StringComparison.OrdinalIgnoreCase));
            if (string.IsNullOrEmpty(selectedContent.Key))
            {
                selectedContent = requestBody.Content.FirstOrDefault(static item => string.Equals(item.Key, "application/x-www-form-urlencoded", StringComparison.OrdinalIgnoreCase));
            }

            if (string.IsNullOrEmpty(selectedContent.Key))
            {
                selectedContent = requestBody.Content.FirstOrDefault(static item => string.Equals(item.Key, "multipart/form-data", StringComparison.OrdinalIgnoreCase));
            }

            if (string.IsNullOrEmpty(selectedContent.Key))
            {
                selectedContent = requestBody.Content.First();
            }

            return new RequestBodyInfo(
                ResolveRequestBodyKind(selectedContent.Key),
                ResolveTypeName(selectedContent.Value.Schema, requestBody.Required),
                requestBody.Required);
        }

        private ResponseInfo ResolveResponse(OpenApiResponses responses)
        {
            var response = responses
                .Where(static item => item.Key.StartsWith("2", StringComparison.Ordinal))
                .OrderBy(static item => ParseResponseStatusCode(item.Key))
                .Select(static item => item.Value)
                .FirstOrDefault();

            if (response?.Content is null || response.Content.Count == 0)
            {
                return new ResponseInfo(ResponseKind.None, string.Empty);
            }

            var selectedContent = response.Content.FirstOrDefault(static item => item.Key.Contains("json", StringComparison.OrdinalIgnoreCase));
            if (string.IsNullOrEmpty(selectedContent.Key))
            {
                selectedContent = response.Content.FirstOrDefault(static item => HasSchemaType(item.Value.Schema, JsonSchemaType.String) && string.Equals(item.Value.Schema?.Format, "binary", StringComparison.OrdinalIgnoreCase));
            }

            if (string.IsNullOrEmpty(selectedContent.Key))
            {
                selectedContent = response.Content.FirstOrDefault(static item => item.Key.StartsWith("text/", StringComparison.OrdinalIgnoreCase));
            }

            if (string.IsNullOrEmpty(selectedContent.Key))
            {
                selectedContent = response.Content.First();
            }

            var kind = ResolveResponseKind(selectedContent.Key, selectedContent.Value.Schema);
            var typeName = kind switch
            {
                ResponseKind.Binary => "byte[]",
                ResponseKind.String => "string",
                ResponseKind.None => string.Empty,
                _ => ResolveTypeName(selectedContent.Value.Schema, required: selectedContent.Value.Schema is null || !IsNullableSchema(selectedContent.Value.Schema))
            };

            return new ResponseInfo(kind, typeName);
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

            var schemaType = schema.Type;
            var format = schema.Format;

            var typeName = schemaType switch
            {
                JsonSchemaType.Integer when string.Equals(format, "int64", StringComparison.OrdinalIgnoreCase) => "long",
                JsonSchemaType.Integer => "int",
                JsonSchemaType.Number when string.Equals(format, "float", StringComparison.OrdinalIgnoreCase) => "float",
                JsonSchemaType.Number => "double",
                JsonSchemaType.Boolean => "bool",
                JsonSchemaType.String when string.Equals(format, "date", StringComparison.OrdinalIgnoreCase) => "DateOnly",
                JsonSchemaType.String when string.Equals(format, "date-time", StringComparison.OrdinalIgnoreCase) => "DateTimeOffset",
                JsonSchemaType.String when string.Equals(format, "uuid", StringComparison.OrdinalIgnoreCase) => "Guid",
                JsonSchemaType.String when string.Equals(format, "binary", StringComparison.OrdinalIgnoreCase) => "byte[]",
                JsonSchemaType.Array => $"IReadOnlyList<{ResolveTypeName(schema.Items, required: true)}>",
                JsonSchemaType.String => "string",
                _ => "JsonElement"
            };

            return required && !IsNullableSchema(schema) ? typeName : $"{typeName}?";
        }

        private static RequestBodyKind ResolveRequestBodyKind(string contentType)
        {
            if (string.Equals(contentType, "application/x-www-form-urlencoded", StringComparison.OrdinalIgnoreCase))
            {
                return RequestBodyKind.FormUrlEncoded;
            }

            if (string.Equals(contentType, "multipart/form-data", StringComparison.OrdinalIgnoreCase))
            {
                return RequestBodyKind.MultipartFormData;
            }

            return RequestBodyKind.Json;
        }

        private static ResponseKind ResolveResponseKind(string contentType, IOpenApiSchema? schema)
        {
            if (HasSchemaType(schema, JsonSchemaType.String) && string.Equals(schema?.Format, "binary", StringComparison.OrdinalIgnoreCase))
            {
                return ResponseKind.Binary;
            }

            if (contentType.Contains("json", StringComparison.OrdinalIgnoreCase))
            {
                return ResponseKind.Json;
            }

            return ResponseKind.String;
        }

        private static int ParseResponseStatusCode(string statusCode)
        {
            return int.TryParse(statusCode, NumberStyles.None, CultureInfo.InvariantCulture, out var value)
                ? value
                : int.MaxValue;
        }

        private static string MakeNullableTypeName(string typeName)
        {
            return typeName.EndsWith("?", StringComparison.Ordinal) ? typeName : $"{typeName}?";
        }

        private static string SafeIdentifier(string value)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                return "value";
            }

            var builder = new StringBuilder(value.Length + 1);
            foreach (var ch in value)
            {
                builder.Append(char.IsLetterOrDigit(ch) || ch == '_' ? ch : '_');
            }

            var sanitized = builder.ToString().Trim('_');
            if (string.IsNullOrEmpty(sanitized))
            {
                sanitized = "value";
            }

            if (!IsIdentifierStartCharacter(sanitized[0]))
            {
                sanitized = "_" + sanitized;
            }

            return s_reservedIdentifiers.Contains(sanitized) ? $"@{sanitized}" : sanitized;
        }

        private static string ToPascalCase(string value)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                return string.Empty;
            }

            var normalized = new StringBuilder(value.Length);
            foreach (var ch in value)
            {
                normalized.Append(char.IsLetterOrDigit(ch) ? ch : ' ');
            }

            var parts = normalized.ToString().Split([' '], StringSplitOptions.RemoveEmptyEntries);
            return string.Concat(parts.Select(static part => char.ToUpperInvariant(part[0]) + part.Substring(1)));
        }

        private static string ToCamelCase(string value)
        {
            var pascal = ToPascalCase(value);
            return string.IsNullOrEmpty(pascal) ? "value" : char.ToLowerInvariant(pascal[0]) + pascal.Substring(1);
        }

        private static bool IsReferenceLikeType(string typeName)
        {
            return typeName is "string"
                || typeName.StartsWith("IReadOnlyList<", StringComparison.Ordinal)
                || (!typeName.EndsWith("?", StringComparison.Ordinal) && typeName is not "int" and not "long" and not "float" and not "double" and not "bool" and not "DateOnly" and not "DateTimeOffset" and not "Guid" and not "JsonElement");
        }

        private static bool RequiresNonNullJsonResponse(string typeName)
        {
            return !typeName.EndsWith("?", StringComparison.Ordinal) && IsReferenceLikeType(typeName);
        }

        private static string EscapeStringLiteral(string value)
        {
            return value.Replace("\\", "\\\\").Replace("\"", "\\\"");
        }

        private static SecuritySchemeBinding? CreateSecuritySchemeBinding(string schemeKey, IOpenApiSecurityScheme scheme)
        {
            if (scheme.Type == SecuritySchemeType.OAuth2
                || (scheme.Type == SecuritySchemeType.Http && string.Equals(scheme.Scheme, "bearer", StringComparison.OrdinalIgnoreCase)))
            {
                var parameterName = SafeIdentifier(ToCamelCase(schemeKey == "oauth2" ? "access_token" : $"{schemeKey}_token"));
                return new SecuritySchemeBinding(
                    parameterName,
                    $"string? {parameterName} = default",
                    "Authorization",
                    SecuritySchemeLocation.Header,
                    isBearerToken: true);
            }

            if (scheme.Type == SecuritySchemeType.ApiKey)
            {
                var parameterName = SafeIdentifier(ToCamelCase($"{schemeKey}_api_key"));
                return new SecuritySchemeBinding(
                    parameterName,
                    $"string? {parameterName} = default",
                    scheme.Name ?? parameterName,
                    MapLocation(scheme.In ?? ParameterLocation.Header),
                    isBearerToken: false);
            }

            return null;
        }

        private static SecuritySchemeLocation MapLocation(ParameterLocation location)
        {
            return location switch
            {
                ParameterLocation.Query => SecuritySchemeLocation.Query,
                ParameterLocation.Cookie => SecuritySchemeLocation.Cookie,
                _ => SecuritySchemeLocation.Header,
            };
        }

        private static void EmitSecuritySchemeInitialization(StringBuilder builder, SecuritySchemeBinding securityScheme)
        {
            if (securityScheme.Location == SecuritySchemeLocation.Query)
            {
                builder.Append("        ").Append(securityScheme.FieldName).Append(" = ").Append(securityScheme.ParameterName).AppendLine(";");
                return;
            }

            builder.Append("        if (").Append(securityScheme.ParameterName).AppendLine(" is not null)");
            builder.AppendLine("        {");
            if (securityScheme.IsBearerToken)
            {
                builder.Append("            _httpClient.DefaultRequestHeaders.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue(\"Bearer\", ").Append(securityScheme.ParameterName).AppendLine(");");
            }
            else if (securityScheme.Location == SecuritySchemeLocation.Cookie)
            {
                builder.Append("            _httpClient.DefaultRequestHeaders.TryAddWithoutValidation(\"Cookie\", \"").Append(securityScheme.HeaderOrParameterName).Append("=\" + ").Append(securityScheme.ParameterName).AppendLine(");");
            }
            else
            {
                builder.Append("            _httpClient.DefaultRequestHeaders.TryAddWithoutValidation(\"").Append(securityScheme.HeaderOrParameterName).Append("\", ").Append(securityScheme.ParameterName).AppendLine(");");
            }
            builder.AppendLine("        }");
        }

        private static string BuildOperationMethodName(string? operationId, string operationType, string route, string? tagName)
        {
            var source = operationId ?? $"{operationType}_{route}";
            var operationTokens = TokenizeWords(source);
            var tagTokens = new HashSet<string>(
                TokenizeWords(tagName ?? string.Empty)
                    .Select(NormalizeToken)
                    .Where(static token => token.Length > 0),
                StringComparer.OrdinalIgnoreCase);

            var filteredTokens = operationTokens
                .Where(token => !tagTokens.Contains(NormalizeToken(token)))
                .ToList();

            if (filteredTokens.Count == 0)
            {
                filteredTokens.Add(operationType.ToLowerInvariant());
            }

            return SafeIdentifier(string.Concat(filteredTokens.Select(static token => ToPascalCase(token ?? string.Empty))));
        }

        private static List<string> TokenizeWords(string value)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                return [];
            }

            var normalized = new StringBuilder(value.Length);
            foreach (var ch in value)
            {
                normalized.Append(char.IsLetterOrDigit(ch) ? ch : ' ');
            }

            return [.. normalized.ToString().Split([' '], StringSplitOptions.RemoveEmptyEntries)];
        }

        private static string NormalizeToken(string value)
        {
            var normalized = value.Trim();
            if (normalized.Length > 2
                && normalized.EndsWith("s", StringComparison.OrdinalIgnoreCase)
                && !normalized.EndsWith("ss", StringComparison.OrdinalIgnoreCase)
                && !normalized.EndsWith("us", StringComparison.OrdinalIgnoreCase))
            {
                normalized = normalized.Substring(0, normalized.Length - 1);
            }

            return normalized;
        }

        private static string? GetTagName(OpenApiOperation operation)
        {
            return operation.Tags?.FirstOrDefault()?.Name;
        }

        private static string BuildClientName(string documentPath, OpenApiDocument document)
        {
            var baseName = Path.GetFileNameWithoutExtension(documentPath);
            if (string.IsNullOrWhiteSpace(baseName))
            {
                baseName = !string.IsNullOrWhiteSpace(document.Info.Title)
                    ? document.Info.Title
                    : "OpenApi";
            }

            var normalized = SafeIdentifier(ToPascalCase(baseName ?? "OpenApi"));
            return normalized.EndsWith("Client", StringComparison.Ordinal) ? normalized : $"{normalized}Client";
        }

        private sealed class TagGroup(string propertyName, string className)
        {
            public string PropertyName { get; } = propertyName;

            public string ClassName { get; } = className;

            public List<OperationGroupItem> Operations { get; } = [];
        }

        private sealed class OperationGroupItem(string route, IOpenApiPathItem pathItem, string operationType, OpenApiOperation operation)
        {
            public string Route { get; } = route;
            public IOpenApiPathItem PathItem { get; } = pathItem;
            public string OperationType { get; } = operationType;
            public OpenApiOperation Operation { get; } = operation;
        }

        private sealed class SecuritySchemeBinding(string parameterName, string parameterDeclaration, string headerOrParameterName, SecuritySchemeLocation location, bool isBearerToken)
        {
            public string ParameterName { get; } = parameterName;
            public string ParameterDeclaration { get; } = parameterDeclaration;
            public string HeaderOrParameterName { get; } = headerOrParameterName;
            public SecuritySchemeLocation Location { get; } = location;
            public bool IsBearerToken { get; } = isBearerToken;
            public string FieldName { get; } = $"_{parameterName}";
        }

        private enum SecuritySchemeLocation
        {
            Header,
            Query,
            Cookie
        }

        private sealed class RequestBodyInfo(RequestBodyKind kind, string typeName, bool isRequired)
        {
            public RequestBodyKind Kind { get; } = kind;
            public string TypeName { get; } = typeName;
            public bool IsRequired { get; } = isRequired;
        }

        private sealed class ResponseInfo(ResponseKind kind, string typeName)
        {
            public ResponseKind Kind { get; } = kind;
            public string TypeName { get; } = typeName;
        }

        private enum RequestBodyKind
        {
            Json,
            FormUrlEncoded,
            MultipartFormData
        }

        private enum ResponseKind
        {
            None,
            Json,
            String,
            Binary
        }

        private static bool IsIdentifierStartCharacter(char ch)
        {
            return ch == '_' || char.IsLetter(ch);
        }

        private static bool HasSchemaType(IOpenApiSchema? schema, JsonSchemaType type)
        {
            return schema?.Type is { } schemaType && (schemaType & type) == type;
        }

        private static bool IsNullableSchema(IOpenApiSchema schema)
        {
            return HasSchemaType(schema, JsonSchemaType.Null);
        }
    }
}
