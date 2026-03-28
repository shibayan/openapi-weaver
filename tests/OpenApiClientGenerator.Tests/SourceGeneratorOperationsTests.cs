using Xunit;

namespace OpenApiClientGenerator.Tests;

public sealed partial class SourceGeneratorRequestResponseTests
{
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
    public void PathAndOperationParameters_AreCombinedIntoMethodSignature()
    {
        const string openApi = """
            openapi: 3.0.1
            info:
              title: Combined Parameters API
              version: v1
            paths:
              /partners/{partner_id}/reports:
                parameters:
                  - name: partner_id
                    in: path
                    required: true
                    schema:
                      type: integer
                get:
                  operationId: list_partner_reports
                  parameters:
                    - name: page
                      in: query
                      required: false
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

        Assert.Contains("public async Task<JsonElement> ListPartnerReportsAsync(int partnerId, int? page = default, CancellationToken cancellationToken = default)", source);
        Assert.Contains("path = path.Replace(\"{partner_id}\", Uri.EscapeDataString(OpenApiClientHelpers.FormatParameter(partnerId)));", source);
        Assert.Contains("query.Add(\"page=\" + Uri.EscapeDataString(OpenApiClientHelpers.FormatParameter(page)));", source);
    }

    [Fact]
    public void OperationDescriptions_AreEmittedAsXmlDocumentationComments()
    {
        const string openApi = """
            openapi: 3.0.1
            info:
              title: Pet API
              description: Client for pet operations.
              version: v1
            tags:
              - name: pets
                description: Operations for managing pets.
            paths:
              /pets/{id}:
                get:
                  operationId: get_pet
                  tags:
                    - pets
                  summary: Gets a pet.
                  description: Returns the pet identified by the provided id.
                  parameters:
                    - name: id
                      in: path
                      required: true
                      description: The pet identifier.
                      schema:
                        type: integer
                  responses:
                    '200':
                      description: The matching pet.
                      content:
                        application/json:
                          schema:
                            type: object
            """;

        var source = GenerateSource(openApi);

        Assert.Contains("/// <summary>", source);
        Assert.Contains("/// Pet API", source);
        Assert.Contains("/// <remarks>", source);
        Assert.Contains("/// Client for pet operations.", source);
        Assert.Contains("/// Gets a pet.", source);
        Assert.Contains("/// Returns the pet identified by the provided id.", source);
        Assert.Contains("/// <param name=\"id\">", source);
        Assert.Contains("/// The pet identifier.", source);
        Assert.Contains("/// <param name=\"cancellationToken\">", source);
        Assert.Contains("/// A cancellation token that can be used to cancel the operation.", source);
        Assert.Contains("/// <returns>", source);
        Assert.Contains("/// The matching pet.", source);
        Assert.Contains("/// Operations for managing pets.", source);
    }

    [Fact]
    public void OperationDocumentation_StripsHtmlTags()
    {
        const string openApi = """
            openapi: 3.0.1
            info:
              title: Pet API
              description: <p>Client <strong>for</strong> pet operations.</p>
              version: v1
            paths:
              /pets/{id}:
                get:
                  operationId: get_pet
                  summary: <strong>Gets</strong> a pet.<br/>Fast.
                  description: <p>Returns the <code>pet</code> identified by the provided id.</p>
                  parameters:
                    - name: id
                      in: path
                      required: true
                      description: <span>The pet</span> identifier.
                      schema:
                        type: integer
                  responses:
                    '200':
                      description: <div>The matching <em>pet</em>.</div>
                      content:
                        application/json:
                          schema:
                            type: object
            """;

        var source = GenerateSource(openApi);

        Assert.Contains("/// Client for pet operations.", source);
        Assert.Contains("/// Gets a pet.", source);
        Assert.Contains("/// Fast.", source);
        Assert.Contains("/// Returns the pet identified by the provided id.", source);
        Assert.Contains("/// The pet identifier.", source);
        Assert.Contains("/// The matching pet.", source);
        Assert.DoesNotContain("<strong>", source);
        Assert.DoesNotContain("<br/>", source);
        Assert.DoesNotContain("<code>", source);
        Assert.DoesNotContain("<div>", source);
    }
}
