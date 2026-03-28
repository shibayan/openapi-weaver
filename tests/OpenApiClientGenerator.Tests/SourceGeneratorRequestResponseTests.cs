using System.Collections.Immutable;

using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Text;

using Xunit;

namespace OpenApiClientGenerator.Tests;

public sealed class SourceGeneratorRequestResponseTests
{
    [Fact]
    public void EmptyDocument_ReportsDiagnostic_AndDoesNotGenerateSource()
    {
        var result = RunGenerator("   ");

        var diagnostic = Assert.Single(result.Diagnostics);
        Assert.Equal("OARSG002", diagnostic.Id);
        Assert.Equal(DiagnosticSeverity.Error, diagnostic.Severity);
        Assert.Empty(result.GeneratedSources);
    }

    [Fact]
    public void InvalidDocument_ReportsDiagnostic_AndDoesNotGenerateSource()
    {
        var result = RunGenerator("not: [valid");

        var diagnostic = Assert.Single(result.Diagnostics);
        Assert.Equal("OARSG004", diagnostic.Id);
        Assert.Equal(DiagnosticSeverity.Error, diagnostic.Severity);
        Assert.Empty(result.GeneratedSources);
    }

    [Fact]
    public void MultipartRequestBody_UsesMultipartContent_AndBinarySchemaType()
    {
        const string openApi = """
            openapi: 3.0.1
            info:
              title: Upload API
              version: v1
            paths:
              /receipts:
                post:
                  operationId: create_receipt
                  requestBody:
                    required: true
                    content:
                      multipart/form-data:
                        schema:
                          $ref: '#/components/schemas/receiptCreateParams'
                  responses:
                    '201':
                      description: created
                      content:
                        application/json:
                          schema:
                            type: object
            components:
              schemas:
                receiptCreateParams:
                  type: object
                  required:
                    - receipt
                    - company_id
                  properties:
                    receipt:
                      type: string
                      format: binary
                    company_id:
                      type: integer
        """;

        var source = GenerateSource(openApi);

        Assert.Contains("public required byte[] Receipt", source);
        Assert.Contains("[JsonPropertyName(\"company_id\")]", source);
        Assert.Contains("request.Content = OpenApiClientHelpers.CreateMultipartFormDataContent(body);", source);
    }

    [Fact]
    public void OptionalFormBody_UsesNullableParameter_AndConditionalContentAssignment()
    {
        const string openApi = """
            openapi: 3.0.1
            info:
              title: Form API
              version: v1
            paths:
              /expense_applications:
                post:
                  operationId: create_expense_application
                  requestBody:
                    required: false
                    content:
                      application/x-www-form-urlencoded:
                        schema:
                          $ref: '#/components/schemas/expenseApplicationCreateParams'
                  responses:
                    '201':
                      description: created
                      content:
                        application/json:
                          schema:
                            type: object
            components:
              schemas:
                expenseApplicationCreateParams:
                  type: object
                  properties:
                    company_id:
                      type: integer
        """;

        var source = GenerateSource(openApi);

        Assert.Contains("ExpenseApplicationCreateParams? body = default", source);
        Assert.Contains("if (body is not null)", source);
        Assert.Contains("request.Content = OpenApiClientHelpers.CreateFormUrlEncodedContent(body!);", source);
    }

    [Fact]
    public void NoContentResponse_GeneratesNonGenericTask()
    {
        const string openApi = """
            openapi: 3.0.1
            info:
              title: Delete API
              version: v1
            paths:
              /receipts/{id}:
                delete:
                  operationId: destroy_receipt
                  parameters:
                    - name: id
                      in: path
                      required: true
                      schema:
                        type: integer
                  responses:
                    '204':
                      description: deleted
                      content: {}
        """;

        var source = GenerateSource(openApi);

        Assert.Contains("public async Task ", source);
        Assert.DoesNotContain("Task<string>", source);
        Assert.Contains("response.EnsureSuccessStatusCode();", source);
        Assert.Contains("return;", source);
    }

    [Fact]
    public void BinaryResponse_GeneratesByteArrayReturnType()
    {
        const string openApi = """
            openapi: 3.0.1
            info:
              title: Download API
              version: v1
            paths:
              /receipts/{id}/download:
                get:
                  operationId: download_receipt
                  parameters:
                    - name: id
                      in: path
                      required: true
                      schema:
                        type: integer
                  responses:
                    '200':
                      description: ok
                      content:
                        application/pdf:
                          schema:
                            type: string
                            format: binary
        """;

        var source = GenerateSource(openApi);

        Assert.Contains("public async Task<byte[]> ", source);
        Assert.Contains("return await response.Content.ReadAsByteArrayAsync(cancellationToken).ConfigureAwait(false);", source);
    }

    [Fact]
    public void SnakeCaseSchema_UsesJsonPropertyNameAttributes()
    {
        const string openApi = """
            openapi: 3.0.1
            info:
              title: Naming API
              version: v1
            paths: {}
            components:
              schemas:
                partnerResponse:
                  type: object
                  required:
                    - company_id
                  properties:
                    company_id:
                      type: integer
                    display_name:
                      type: string
        """;

        var source = GenerateSource(openApi);

        Assert.Contains("[JsonPropertyName(\"company_id\")]", source);
        Assert.Contains("public required int CompanyId { get; init; }", source);
        Assert.Contains("[JsonPropertyName(\"display_name\")]", source);
        Assert.Contains("public string? DisplayName { get; init; }", source);
    }

    [Fact]
    public void RequestBody_WithJsonAndFormMediaTypes_PrefersJson()
    {
        const string openApi = """
            openapi: 3.0.1
            info:
              title: Mixed Request API
              version: v1
            paths:
              /partners:
                post:
                  operationId: create_partner
                  requestBody:
                    required: true
                    content:
                      application/json:
                        schema:
                          $ref: '#/components/schemas/partnerCreateParams'
                      application/x-www-form-urlencoded:
                        schema:
                          $ref: '#/components/schemas/partnerCreateParams'
                  responses:
                    '201':
                      description: created
                      content:
                        application/json:
                          schema:
                            type: object
            components:
              schemas:
                partnerCreateParams:
                  type: object
                  properties:
                    company_id:
                      type: integer
        """;

        var source = GenerateSource(openApi);

        Assert.Contains("request.Content = JsonContent.Create(body, options: OpenApiClientHelpers.SerializerOptions);", source);
        Assert.DoesNotContain("OpenApiClientHelpers.CreateFormUrlEncodedContent(body)", source);
    }

    [Fact]
    public void TextResponse_UsesStringReturnType()
    {
        const string openApi = """
            openapi: 3.0.1
            info:
              title: Text API
              version: v1
            paths:
              /reports:
                get:
                  operationId: download_report
                  responses:
                    '200':
                      description: ok
                      content:
                        text/plain:
                          schema:
                            type: string
        """;

        var source = GenerateSource(openApi);

        Assert.Contains("public async Task<string> ", source);
        Assert.Contains("return await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);", source);
    }

    [Fact]
    public void MultipleSuccessResponses_PrefersLowest2xxWithBody()
    {
        const string openApi = """
            openapi: 3.0.1
            info:
              title: Multi Success API
              version: v1
            paths:
              /pets:
                post:
                  operationId: create_pet
                  responses:
                    '201':
                      description: created
                      content:
                        application/json:
                          schema:
                            $ref: '#/components/schemas/petResponse'
                    '204':
                      description: no content
                      content: {}
            components:
              schemas:
                petResponse:
                  type: object
                  required:
                    - id
                  properties:
                    id:
                      type: integer
        """;

        var source = GenerateSource(openApi);

        Assert.Contains("public async Task<PetResponse> ", source);
        Assert.Contains("ReadFromJsonAsync<PetResponse>", source);
        Assert.DoesNotContain("public async Task CreatePetAsync", source);
    }

    [Fact]
    public void NullableJsonResponse_DoesNotForceNonNullBody()
    {
        const string openApi = """
            openapi: 3.0.1
            info:
              title: Nullable Response API
              version: v1
            paths:
              /partners/{id}:
                get:
                  operationId: get_partner
                  parameters:
                    - name: id
                      in: path
                      required: true
                      schema:
                        type: integer
                  responses:
                    '200':
                      description: ok
                      content:
                        application/json:
                          schema:
                            nullable: true
                            type: object
                            properties:
                              company_id:
                                type: integer
        """;

        var source = GenerateSource(openApi);

        Assert.Contains("public async Task<JsonElement?> ", source);
        Assert.Contains("return await response.Content.ReadFromJsonAsync<JsonElement?>", source);
        Assert.DoesNotContain("The response body was empty.", source);
    }

    [Fact]
    public void QueryApiKeySecurityScheme_GeneratesConstructorParameter_AndAppendsToPath()
    {
        const string openApi = """
            openapi: 3.0.1
            info:
              title: Query Security API
              version: v1
            paths:
              /reports:
                get:
                  operationId: list_reports
                  responses:
                    '200':
                      description: ok
                      content:
                        application/json:
                          schema:
                            type: object
            components:
              securitySchemes:
                partner:
                  type: apiKey
                  in: query
                  name: api_key
        """;

        var source = GenerateSource(openApi);

        Assert.Contains("private readonly string? _partnerApiKey;", source);
        Assert.Contains("public partial class TestClient : IDisposable", source);
        Assert.Contains("public TestClient(string? partnerApiKey = default)", source);
        Assert.Contains("_partnerApiKey = partnerApiKey;", source);
        Assert.Contains("path += \"api_key=\" + Uri.EscapeDataString(_partnerApiKey);", source);
    }

    [Fact]
    public void BearerSecurityScheme_GeneratesAuthorizationHeaderInitialization()
    {
        const string openApi = """
            openapi: 3.0.1
            info:
              title: Bearer Security API
              version: v1
            paths:
              /reports:
                get:
                  operationId: list_reports
                  responses:
                    '200':
                      description: ok
                      content:
                        application/json:
                          schema:
                            type: object
            components:
              securitySchemes:
                partner:
                  type: http
                  scheme: bearer
        """;

        var source = GenerateSource(openApi);

        Assert.Contains("public TestClient(string? partnerToken = default)", source);
        Assert.Contains("_httpClient.DefaultRequestHeaders.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue(\"Bearer\", partnerToken);", source);
    }

    [Fact]
    public void OperationIdMatchingTag_FallsBackToHttpVerbMethodName()
    {
        const string openApi = """
            openapi: 3.0.1
            info:
              title: Naming API
              version: v1
            paths:
              /receipts:
                get:
                  operationId: receipts
                  tags:
                    - receipts
                  responses:
                    '200':
                      description: ok
                      content:
                        application/json:
                          schema:
                            type: object
        """;

        var source = GenerateSource(openApi);

        Assert.Contains("public async Task<JsonElement> GetAsync(", source);
        Assert.DoesNotContain("ReceiptsAsync", source);
    }

    [Fact]
    public void EnumSchema_GeneratesEnumType()
    {
        const string openApi = """
            openapi: 3.0.1
            info:
              title: Enum API
              version: v1
            paths: {}
            components:
              schemas:
                orderStatus:
                  type: string
                  enum:
                    - pending
                    - processing
                    - completed
                    - cancelled
            """;

        var source = GenerateSource(openApi);

        Assert.Contains("public readonly record struct OrderStatus(string Value)", source);
        Assert.Contains("public static readonly OrderStatus Pending = new(\"pending\");", source);
        Assert.Contains("public static readonly OrderStatus Processing = new(\"processing\");", source);
        Assert.Contains("public static readonly OrderStatus Completed = new(\"completed\");", source);
        Assert.Contains("public static readonly OrderStatus Cancelled = new(\"cancelled\");", source);
        Assert.Contains("public override string ToString() => Value;", source);
        Assert.Contains("public sealed class OrderStatusJsonConverter : JsonConverter<OrderStatus>", source);
    }

    [Fact]
    public void PathParameter_ReplacesPlaceholderInRoute()
    {
        const string openApi = """
            openapi: 3.0.1
            info:
              title: Path Param API
              version: v1
            paths:
              /partners/{partner_id}/items/{item_id}:
                get:
                  operationId: get_partner_item
                  parameters:
                    - name: partner_id
                      in: path
                      required: true
                      schema:
                        type: integer
                    - name: item_id
                      in: path
                      required: true
                      schema:
                        type: integer
                  responses:
                    '200':
                      description: ok
                      content:
                        application/json:
                          schema:
                            type: object
            """;

        var source = GenerateSource(openApi);

        Assert.Contains("path = path.Replace(\"{partner_id}\", Uri.EscapeDataString(OpenApiClientHelpers.FormatParameter(partnerId)));", source);
        Assert.Contains("path = path.Replace(\"{item_id}\", Uri.EscapeDataString(OpenApiClientHelpers.FormatParameter(itemId)));", source);
    }

    [Fact]
    public void HeaderParameter_AddsHeaderToRequest()
    {
        const string openApi = """
            openapi: 3.0.1
            info:
              title: Header API
              version: v1
            paths:
              /reports:
                get:
                  operationId: list_reports
                  parameters:
                    - name: X-Custom-Header
                      in: header
                      required: false
                      schema:
                        type: string
                  responses:
                    '200':
                      description: ok
                      content:
                        application/json:
                          schema:
                            type: object
            """;

        var source = GenerateSource(openApi);

        Assert.Contains("request.Headers.TryAddWithoutValidation(\"X-Custom-Header\"", source);
        Assert.Contains("if (xCustomHeader is not null)", source);
    }

    [Fact]
    public void ServerUrl_SetsBaseAddress()
    {
        const string openApi = """
            openapi: 3.0.1
            info:
              title: Server URL API
              version: v1
            servers:
              - url: https://api.example.com/v1
            paths:
              /reports:
                get:
                  operationId: list_reports
                  responses:
                    '200':
                      description: ok
                      content:
                        application/json:
                          schema:
                            type: object
            """;

        var source = GenerateSource(openApi);

        Assert.Contains("_httpClient.BaseAddress = new Uri(\"https://api.example.com/v1\", UriKind.Absolute);", source);
    }

    [Fact]
    public void MultipleSecuritySchemes_GeneratesMultipleConstructorParameters()
    {
        const string openApi = """
            openapi: 3.0.1
            info:
              title: Multi Security API
              version: v1
            paths:
              /reports:
                get:
                  operationId: list_reports
                  responses:
                    '200':
                      description: ok
                      content:
                        application/json:
                          schema:
                            type: object
            components:
              securitySchemes:
                oauth2:
                  type: oauth2
                  flows:
                    authorizationCode:
                      authorizationUrl: https://example.com/auth
                      tokenUrl: https://example.com/token
                partner:
                  type: apiKey
                  in: query
                  name: api_key
            """;

        var source = GenerateSource(openApi);

        Assert.Contains("accessToken", source);
        Assert.Contains("partnerApiKey", source);
        Assert.Contains("Authorization", source);
        Assert.Contains("api_key", source);
    }

    [Fact]
    public void QueryParameters_UseExpectedTypes_AndBuildQueryString()
    {
        const string openApi = """
            openapi: 3.0.1
            info:
              title: Query Parameter API
              version: v1
            paths:
              /reports:
                get:
                  operationId: list_reports
                  parameters:
                    - name: report_date
                      in: query
                      required: true
                      schema:
                        type: string
                        format: date
                    - name: changed_after
                      in: query
                      required: false
                      schema:
                        type: string
                        format: date-time
                    - name: request_id
                      in: query
                      required: false
                      schema:
                        type: string
                        format: uuid
                  responses:
                    '200':
                      description: ok
                      content:
                        application/json:
                          schema:
                            type: object
            """;

        var source = GenerateSource(openApi);

        Assert.Contains("public async Task<JsonElement> ListReportsAsync(DateOnly reportDate, DateTimeOffset? changedAfter = default, Guid? requestId = default, CancellationToken cancellationToken = default)", source);
        Assert.Contains("var query = new List<string>();", source);
        Assert.Contains("query.Add(\"report_date=\" + Uri.EscapeDataString(OpenApiClientHelpers.FormatParameter(reportDate)));", source);
        Assert.Contains("if (changedAfter is not null)", source);
        Assert.Contains("query.Add(\"changed_after=\" + Uri.EscapeDataString(OpenApiClientHelpers.FormatParameter(changedAfter)));", source);
        Assert.Contains("if (requestId is not null)", source);
        Assert.Contains("query.Add(\"request_id=\" + Uri.EscapeDataString(OpenApiClientHelpers.FormatParameter(requestId)));", source);
    }

    [Fact]
    public void ArrayJsonResponse_GeneratesReadOnlyListReturnType_AndNonNullGuard()
    {
        const string openApi = """
            openapi: 3.0.1
            info:
              title: Array Response API
              version: v1
            paths:
              /tags:
                get:
                  operationId: list_tags
                  responses:
                    '200':
                      description: ok
                      content:
                        application/json:
                          schema:
                            type: array
                            items:
                              type: string
            """;

        var source = GenerateSource(openApi);

        Assert.Contains("public async Task<IReadOnlyList<string>> ListTagsAsync(CancellationToken cancellationToken = default)", source);
        Assert.Contains("return await response.Content.ReadFromJsonAsync<IReadOnlyList<string>>(OpenApiClientHelpers.SerializerOptions, cancellationToken).ConfigureAwait(false)", source);
        Assert.Contains("?? throw new InvalidOperationException(\"The response body was empty.\");", source);
    }

    [Fact]
    public void HeaderApiKeySecurityScheme_GeneratesDefaultHeaderInitialization()
    {
        const string openApi = """
            openapi: 3.0.1
            info:
              title: Header Security API
              version: v1
            paths:
              /reports:
                get:
                  operationId: list_reports
                  responses:
                    '200':
                      description: ok
                      content:
                        application/json:
                          schema:
                            type: object
            components:
              securitySchemes:
                partner:
                  type: apiKey
                  in: header
                  name: X-Partner-Key
            """;

        var source = GenerateSource(openApi);

        Assert.Contains("public TestClient(string? partnerApiKey = default)", source);
        Assert.Contains("_httpClient.DefaultRequestHeaders.TryAddWithoutValidation(\"X-Partner-Key\", partnerApiKey);", source);
    }

    [Fact]
    public void CookieApiKeySecurityScheme_GeneratesCookieHeaderInitialization()
    {
        const string openApi = """
            openapi: 3.0.1
            info:
              title: Cookie Security API
              version: v1
            paths:
              /reports:
                get:
                  operationId: list_reports
                  responses:
                    '200':
                      description: ok
                      content:
                        application/json:
                          schema:
                            type: object
            components:
              securitySchemes:
                session:
                  type: apiKey
                  in: cookie
                  name: session_id
            """;

        var source = GenerateSource(openApi);

        Assert.Contains("public TestClient(string? sessionApiKey = default)", source);
        Assert.Contains("_httpClient.DefaultRequestHeaders.TryAddWithoutValidation(\"Cookie\", \"session_id=\" + sessionApiKey);", source);
    }

    private static string GenerateSource(string openApi)
    {
        var result = RunGenerator(openApi);
        Assert.DoesNotContain(result.Diagnostics, static diagnostic => diagnostic.Severity == DiagnosticSeverity.Error);
        return Assert.Single(result.GeneratedSources);
    }

    private static readonly ImmutableArray<MetadataReference> s_metadataReferences = [..
        ((string?)AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES"))!
            .Split(Path.PathSeparator)
            .Distinct()
            .Select(static path => (MetadataReference)MetadataReference.CreateFromFile(path))];

    private static GeneratorTestResult RunGenerator(string openApi)
    {
        var generator = new OpenApiClientSourceGenerator();
        var parseOptions = new CSharpParseOptions(LanguageVersion.Preview);
        var syntaxTree = CSharpSyntaxTree.ParseText("public sealed class Marker {}", parseOptions);

        var compilation = CSharpCompilation.Create(
            "GeneratorTests",
            [syntaxTree],
            s_metadataReferences,
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));

        var additionalText = new InMemoryAdditionalText("test.yaml", openApi);
        GeneratorDriver driver = CSharpGeneratorDriver.Create(
            generators: [generator.AsSourceGenerator()],
            additionalTexts: [additionalText],
            parseOptions: parseOptions);

        driver = driver.RunGeneratorsAndUpdateCompilation(compilation, out var outputCompilation, out var diagnostics);

        var generatorDiagnostics = driver.GetRunResult()
            .Results
            .SelectMany(static result => result.Diagnostics)
            .ToArray();

        var compilationErrors = diagnostics
            .Concat(outputCompilation.GetDiagnostics())
            .Where(static diagnostic => diagnostic.Severity == DiagnosticSeverity.Error
                && diagnostic.Id != "OARSG002"
                && diagnostic.Id != "OARSG004")
            .ToArray();

        Assert.True(compilationErrors.Length == 0, string.Join(Environment.NewLine, compilationErrors.Select(static error => error.ToString())));

        var generatedSources = driver.GetRunResult()
            .Results
            .SelectMany(static result => result.GeneratedSources)
            .Select(static source => source.SourceText.ToString())
            .ToArray();

        return new GeneratorTestResult(generatorDiagnostics, generatedSources);
    }

    private sealed class InMemoryAdditionalText(string path, string content) : AdditionalText
    {
        public override string Path { get; } = path;

        public override SourceText GetText(CancellationToken cancellationToken = default)
            => SourceText.From(content);
    }

    private sealed class GeneratorTestResult(
        IReadOnlyList<Diagnostic> diagnostics,
        IReadOnlyList<string> generatedSources)
    {
        public IReadOnlyList<Diagnostic> Diagnostics { get; } = diagnostics;

        public IReadOnlyList<string> GeneratedSources { get; } = generatedSources;
    }
}
