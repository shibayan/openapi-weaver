using System.Net;
using System.Net.Sockets;
using System.Reflection;
using System.Text;

using Xunit;

namespace OpenApiWeaver.Tests;

public sealed partial class ClientGeneratorTests
{
    [Fact]
    public void QueryParameter_WithInlineEnum_GeneratesEnumType()
    {
        const string openApi = """
            openapi: 3.0.1
            info:
              title: Pet API
              version: v1
            paths:
              /pet/findByStatus:
                get:
                  tags:
                    - pet
                  operationId: findPetsByStatus
                  parameters:
                    - name: status
                      in: query
                      required: true
                      schema:
                        type: string
                        default: available
                        enum:
                          - available
                          - pending
                          - sold
                  responses:
                    '204':
                      description: ok
        """;

        var source = GenerateSource(openApi);

        Assert.Contains("public async Task FindPetsByStatusAsync(FindPetsByStatusStatusEnum status, CancellationToken cancellationToken = default)", source);
        Assert.Contains("public readonly record struct FindPetsByStatusStatusEnum(string Value)", source);
        Assert.Contains("public static readonly FindPetsByStatusStatusEnum Available = new(\"available\");", source);
        Assert.Contains("public static readonly FindPetsByStatusStatusEnum Pending = new(\"pending\");", source);
        Assert.Contains("public static readonly FindPetsByStatusStatusEnum Sold = new(\"sold\");", source);
    }

    [Fact]
    public void TagNamesThatNormalizeToSameIdentifier_GenerateDistinctTagClients()
    {
        const string openApi = """
            openapi: 3.0.1
            info:
              title: Tag Collision API
              version: v1
            paths:
              /pet-store:
                get:
                  tags:
                    - pet-store
                  operationId: list_pet_store
                  responses:
                    '200':
                      description: ok
                      content:
                        application/json:
                          schema:
                            type: object
              /pet_store:
                get:
                  tags:
                    - pet_store
                  operationId: list_pet_store_alt
                  responses:
                    '200':
                      description: ok
                      content:
                        application/json:
                          schema:
                            type: object
        """;

        var source = GenerateSource(openApi);

        Assert.Contains("public sealed class PetStoreClient", source);
        Assert.Contains("public sealed class PetStore2Client", source);
        Assert.Contains("public PetStoreClient PetStore { get; }", source);
        Assert.Contains("public PetStore2Client PetStore2 { get; }", source);
    }

    [Fact]
    public void MethodAndParameterNamesThatNormalizeToSameIdentifier_GenerateUniqueNames()
    {
        const string openApi = """
            openapi: 3.0.1
            info:
              title: Method Collision API
              version: v1
            paths:
              /reports/by-id:
                post:
                  tags:
                    - reports
                  operationId: get-report
                  parameters:
                    - name: user-id
                      in: query
                      required: true
                      schema:
                        type: integer
                    - name: user_id
                      in: query
                      required: true
                      schema:
                        type: integer
                    - name: body
                      in: query
                      required: false
                      schema:
                        type: string
                    - name: cancellation_token
                      in: query
                      required: false
                      schema:
                        type: string
                  requestBody:
                    required: true
                    content:
                      application/json:
                        schema:
                          $ref: '#/components/schemas/reportCreateParams'
                  responses:
                    '200':
                      description: ok
                      content:
                        application/json:
                          schema:
                            type: object
              /reports/by_id:
                post:
                  tags:
                    - reports
                  operationId: get_report
                  requestBody:
                    required: true
                    content:
                      application/json:
                        schema:
                          $ref: '#/components/schemas/reportCreateParams'
                  responses:
                    '200':
                      description: ok
                      content:
                        application/json:
                          schema:
                            type: object
            components:
              schemas:
                reportCreateParams:
                  type: object
                  properties:
                    name:
                      type: string
        """;

        var source = GenerateSource(openApi);

        Assert.Contains("GetAsync(int userId, int userId2, ReportCreateParams body2, string? body = default, string? cancellationToken2 = default, CancellationToken cancellationToken = default)", source);
        Assert.Contains("Get2Async(ReportCreateParams body, CancellationToken cancellationToken = default)", source);
        Assert.Contains("request.Content = JsonContent.Create(body2, mediaType: System.Net.Http.Headers.MediaTypeHeaderValue.Parse(\"application/json\"), options: OpenApiClientHelpers.SerializerOptions);", source);
        Assert.Contains("OpenApiClientHelpers.AppendQueryParameter(pathBuilder, ref hasQuery, \"user-id\", OpenApiClientHelpers.FormatParameter(userId));", source);
        Assert.Contains("OpenApiClientHelpers.AppendQueryParameter(pathBuilder, ref hasQuery, \"user_id\", OpenApiClientHelpers.FormatParameter(userId2));", source);
    }

    [Fact]
    public void QueryParameter_WithInlineObject_RemainsJsonElement()
    {
        const string openApi = """
            openapi: 3.0.1
            info:
              title: Pet API
              version: v1
            paths:
              /pet/find:
                get:
                  operationId: find_pets
                  parameters:
                    - name: filter
                      in: query
                      required: false
                      schema:
                        type: object
                        properties:
                          status:
                            type: string
                  responses:
                    '204':
                      description: ok
        """;

        var source = GenerateSource(openApi);

        Assert.Contains("public async Task FindPetsAsync(JsonElement? filter = default, CancellationToken cancellationToken = default)", source);
        Assert.DoesNotContain("FindPetsFilterModel", source);
    }

    [Fact]
    public void JsonRequestBody_WithInlineEnum_GeneratesEnumType()
    {
        const string openApi = """
            openapi: 3.0.1
            info:
              title: Pet API
              version: v1
            paths:
              /pet/status:
                post:
                  operationId: create_pet_status
                  requestBody:
                    required: true
                    content:
                      application/json:
                        schema:
                          type: string
                          enum:
                            - available
                            - pending
                            - sold
                  responses:
                    '204':
                      description: ok
        """;

        var source = GenerateSource(openApi);

        Assert.Contains("public async Task CreatePetStatusAsync(CreatePetStatusBodyEnum body, CancellationToken cancellationToken = default)", source);
        Assert.Contains("public readonly record struct CreatePetStatusBodyEnum(string Value)", source);
        Assert.Contains("public static readonly CreatePetStatusBodyEnum Available = new(\"available\");", source);
    }

    [Fact]
    public void JsonResponse_WithInlineEnum_GeneratesEnumType()
    {
        const string openApi = """
            openapi: 3.0.1
            info:
              title: Pet API
              version: v1
            paths:
              /pet/status:
                get:
                  operationId: get_pet_status
                  responses:
                    '200':
                      description: ok
                      content:
                        application/json:
                          schema:
                            type: string
                            enum:
                              - available
                              - pending
                              - sold
        """;

        var source = GenerateSource(openApi);

        Assert.Contains("public async Task<GetPetStatusResponseEnum> GetPetStatusAsync(CancellationToken cancellationToken = default)", source);
        Assert.Contains("public readonly record struct GetPetStatusResponseEnum(string Value)", source);
        Assert.Contains("return await response.Content.ReadFromJsonAsync<GetPetStatusResponseEnum>(OpenApiClientHelpers.SerializerOptions, cancellationToken).ConfigureAwait(false)", source);
    }

    [Fact]
    public void JsonResponse_WithEmptyInlineObject_GeneratesModelType()
    {
        const string openApi = """
            openapi: 3.0.1
            info:
              title: Pet API
              version: v1
            paths:
              /pet/status:
                get:
                  operationId: get_pet_status
                  responses:
                    '200':
                      description: ok
                      content:
                        application/json:
                          schema:
                            type: object
        """;

        var source = GenerateSource(openApi);

        Assert.Contains("public async Task<GetPetStatusResponseModel> GetPetStatusAsync(CancellationToken cancellationToken = default)", source);
        Assert.Contains("public sealed class GetPetStatusResponseModel", source);
        Assert.Contains("return await response.Content.ReadFromJsonAsync<GetPetStatusResponseModel>(OpenApiClientHelpers.SerializerOptions, cancellationToken).ConfigureAwait(false)", source);
    }

    [Fact]
    public void ErrorJsonResponse_WithInlineEnum_GeneratesEnumType()
    {
        const string openApi = """
            openapi: 3.0.1
            info:
              title: Pet API
              version: v1
            paths:
              /pet/status:
                get:
                  operationId: get_pet_status
                  responses:
                    '204':
                      description: ok
                    '400':
                      description: invalid status
                      content:
                        application/json:
                          schema:
                            type: string
                            enum:
                              - invalid
                              - missing
        """;

        var source = GenerateSource(openApi);

        Assert.Contains("public readonly record struct GetPetStatusError400ResponseEnum(string Value)", source);
        Assert.Contains("var error = OpenApiClientHelpers.DeserializeResponseContent<GetPetStatusError400ResponseEnum>(responseContent);", source);
        Assert.Contains("throw new OpenApiException<GetPetStatusError400ResponseEnum>(statusCode, response.ReasonPhrase, contentType, responseContent, error);", source);
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
        Assert.DoesNotContain("PropertyCache<TBody>", source);
        Assert.Contains("var content = new MultipartFormDataContent();", source);
        Assert.Contains("content.Add(new ByteArrayContent(body.Receipt), \"receipt\", \"receipt\");", source);
        Assert.Contains("content.Add(new StringContent(OpenApiClientHelpers.FormatParameter(body.CompanyId)), \"company_id\");", source);
        Assert.Contains("request.Content = content;", source);
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
        Assert.DoesNotContain("PropertyCache<TBody>", source);
        Assert.Contains("if (body is not null)", source);
        Assert.Contains("var values = new List<KeyValuePair<string, string>>();", source);
        Assert.Contains("if (body!.CompanyId is not null)", source);
        Assert.Contains("values.Add(new KeyValuePair<string, string>(\"company_id\", OpenApiClientHelpers.FormatParameter(body!.CompanyId)));", source);
        Assert.Contains("request.Content = new FormUrlEncodedContent(values);", source);
    }

    [Fact]
    public void FormUrlEncodedRequestBody_WithReadOnlyShadowingWritableProperty_AlignsPropertyNames()
    {
        const string openApi = """
            openapi: 3.0.1
            info:
              title: Form API
              version: v1
            paths:
              /reports:
                post:
                  operationId: create_report
                  requestBody:
                    required: true
                    content:
                      application/x-www-form-urlencoded:
                        schema:
                          $ref: '#/components/schemas/ReportForm'
                  responses:
                    '204':
                      description: ok
            components:
              schemas:
                ReportForm:
                  type: object
                  properties:
                    Foo:
                      type: string
                      readOnly: true
                    foo:
                      type: string
            """;

        var source = GenerateSource(openApi);

        // Schema emits both properties: readonly Foo and writable Foo2 (collision resolved via suffix).
        Assert.Contains("public string? Foo { get; private init; }", source);
        Assert.Contains("public string? Foo2 { get; init; }", source);

        // form-urlencoded body must reference the *writable* Foo2 (mapped from JSON name "foo"),
        // not the read-only Foo. Before the fix the RequestBody property name was independently
        // allocated as "Foo", causing the body to be wired to the read-only property.
        Assert.Contains("values.Add(new KeyValuePair<string, string>(\"foo\", OpenApiClientHelpers.FormatParameter(body.Foo2)));", source);
        Assert.DoesNotContain("values.Add(new KeyValuePair<string, string>(\"foo\", OpenApiClientHelpers.FormatParameter(body.Foo))", source);
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
        Assert.Contains("if (!response.IsSuccessStatusCode)", source);
        Assert.Contains("throw new OpenApiException(statusCode, response.ReasonPhrase, contentType, responseContent);", source);
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
    public void BinaryResponse_UsingReferencedSchema_GeneratesByteArrayReturnType()
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
                            $ref: '#/components/schemas/binaryPayload'
            components:
              schemas:
                binaryPayload:
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

        Assert.Contains("request.Content = JsonContent.Create(body, mediaType: System.Net.Http.Headers.MediaTypeHeaderValue.Parse(\"application/json\"), options: OpenApiClientHelpers.SerializerOptions);", source);
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
    public void MultipleSuccessResponses_PrefersBodyOverLowerNoContentStatus()
    {
        const string openApi = """
            openapi: 3.0.1
            info:
              title: Multi Success Preference API
              version: v1
            paths:
              /pets:
                post:
                  operationId: create_pet
                  responses:
                    '200':
                      description: accepted without body
                      content: {}
                    '201':
                      description: created
                      content:
                        application/json:
                          schema:
                            $ref: '#/components/schemas/petResponse'
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
        Assert.Contains("/// created", source);
        Assert.DoesNotContain("public async Task CreatePetAsync", source);
    }

    [Fact]
    public void MultipleSuccessResponses_IgnoresSchemaLessMediaTypeWhenSelectingBodyResponse()
    {
        const string openApi = """
            openapi: 3.0.1
            info:
              title: Multi Success Shell API
              version: v1
            paths:
              /pets:
                post:
                  operationId: create_pet
                  responses:
                    '200':
                      description: placeholder media type
                      content:
                        application/json: {}
                    '201':
                      description: created
                      content:
                        application/json:
                          schema:
                            $ref: '#/components/schemas/petResponse'
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
        Assert.Contains("/// created", source);
        Assert.DoesNotContain("ReadFromJsonAsync<string>", source);
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

        Assert.Contains("public async Task<GetPartnerResponseModel?> ", source);
        Assert.Contains("return await response.Content.ReadFromJsonAsync<GetPartnerResponseModel?>", source);
        Assert.DoesNotContain("The response body was empty.", source);
    }

    [Fact]
    public void NullableReferencedJsonResponse_DoesNotForceNonNullBody()
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
                            $ref: '#/components/schemas/partnerResponse'
            components:
              schemas:
                partnerResponse:
                  nullable: true
                  type: object
                  properties:
                    company_id:
                      type: integer
            """;

        var source = GenerateSource(openApi);

        Assert.Contains("public async Task<PartnerResponse?> GetPartnerAsync", source);
        Assert.Contains("return await response.Content.ReadFromJsonAsync<PartnerResponse?>", source);
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

        Assert.Contains("public async Task<ReceiptsResponseModel> ListAsync(", source);
        Assert.Contains("public sealed class ReceiptsResponseModel", source);
        Assert.DoesNotContain("ReceiptsAsync", source);
    }

    [Fact]
    public void CollectionAndSingleResourceGet_UseListAndGetMethodNames()
    {
        const string openApi = """
            openapi: 3.0.1
            info:
              title: Companies API
              version: v1
            paths:
              /companies:
                get:
                  operationId: get_companies
                  tags:
                    - companies
                  responses:
                    '200':
                      description: ok
                      content:
                        application/json:
                          schema:
                            type: object
              /companies/{id}:
                get:
                  operationId: get_company
                  tags:
                    - companies
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
                            type: object
        """;

        var source = GenerateSource(openApi);

        Assert.Contains("public async Task<GetCompaniesResponseModel> ListAsync(", source);
        Assert.Contains("public async Task<GetCompanyResponseModel> GetAsync(", source);
        Assert.Contains("int id", source);
        Assert.DoesNotContain("GetCompanyAsync", source);
        Assert.DoesNotContain("GetCompaniesAsync", source);
    }

    [Fact]
    public void CanonicalGetMethodNames_UseStableResponseTypeNames()
    {
        const string openApi = """
            openapi: 3.0.1
            info:
              title: Stable Naming API
              version: v1
            paths:
              /companies/{id}:
                get:
                  operationId: get_company
                  tags:
                    - companies
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
                            type: object
              /partners/{id}:
                get:
                  operationId: get_partner
                  tags:
                    - partners
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
                            type: object
        """;

        var source = GenerateSource(openApi);

        Assert.Contains("public sealed class GetCompanyResponseModel", source);
        Assert.Contains("public sealed class GetPartnerResponseModel", source);
        Assert.DoesNotContain("public sealed class GetResponseModel2", source);
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

        Assert.Contains("var pathBuilder = new StringBuilder();", source);
        Assert.Contains("pathBuilder.Append(Uri.EscapeDataString(OpenApiClientHelpers.FormatParameter(partnerId)));", source);
        Assert.Contains("pathBuilder.Append(Uri.EscapeDataString(OpenApiClientHelpers.FormatParameter(itemId)));", source);
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
    public void RequiredHeaderParameter_OmitsNullGuard()
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
                    - name: X-Request-Id
                      in: header
                      required: true
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

        Assert.Contains("request.Headers.TryAddWithoutValidation(\"X-Request-Id\"", source);
        Assert.DoesNotContain("if (xRequestId is not null)", source);
    }

    [Fact]
    public void RequiredHeaderParameter_WithValueType_DoesNotEmitNullCheck()
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
                    - name: X-Page-Number
                      in: header
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

        Assert.Contains("int xPageNumber,", source);
        Assert.DoesNotContain("if (xPageNumber is not null)", source);
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

        Assert.Contains("_httpClient.BaseAddress = new Uri(\"https://api.example.com/v1/\", UriKind.Absolute);", source);
        Assert.Contains("var path = \"reports\";", source);
        Assert.DoesNotContain("CreateRequestUri", source);
    }

    [Fact]
    public async Task BaseAddressPath_IsPreserved_WhenOpenApiRouteStartsWithSlash()
    {
        await using var server = TestHttpServer.Start();

        var openApi = $$"""
            openapi: 3.0.1
            info:
              title: Test
              version: v1
            servers:
              - url: {{server.BaseAddress}}
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

        using var generatedAssembly = LoadGeneratedAssembly(openApi);

        var assembly = generatedAssembly.Assembly;
        var clientType = Assert.Single(assembly.GetTypes(), static type => type.Name == "TestClient");
        var client = Activator.CreateInstance(clientType);
        Assert.NotNull(client);

        var operationProperty = Assert.Single(clientType.GetProperties(), static property => property.PropertyType.GetMethod("ListReportsAsync") is not null);
        var operationClient = operationProperty.GetValue(client);
        Assert.NotNull(operationClient);

        var operationMethod = operationClient.GetType().GetMethod("ListReportsAsync", BindingFlags.Instance | BindingFlags.Public);
        Assert.NotNull(operationMethod);

        var invocation = Assert.IsAssignableFrom<Task>(operationMethod.Invoke(operationClient, [CancellationToken.None]));
        await invocation;

        var requestTarget = await server.GetRequestTargetAsync();
        Assert.Equal("/api/v1/reports", requestTarget);
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

        Assert.Contains("public async Task<ListReportsResponseModel> ListReportsAsync(DateOnly reportDate, DateTimeOffset? changedAfter = default, Guid? requestId = default, CancellationToken cancellationToken = default)", source);
        Assert.Contains("var pathBuilder = new StringBuilder();", source);
        Assert.Contains("var hasQuery = false;", source);
        Assert.Contains("OpenApiClientHelpers.AppendQueryParameter(pathBuilder, ref hasQuery, \"report_date\", OpenApiClientHelpers.FormatParameter(reportDate));", source);
        Assert.Contains("if (changedAfter is not null)", source);
        Assert.Contains("OpenApiClientHelpers.AppendQueryParameter(pathBuilder, ref hasQuery, \"changed_after\", OpenApiClientHelpers.FormatParameter(changedAfter));", source);
        Assert.Contains("if (requestId is not null)", source);
        Assert.Contains("OpenApiClientHelpers.AppendQueryParameter(pathBuilder, ref hasQuery, \"request_id\", OpenApiClientHelpers.FormatParameter(requestId));", source);
    }

    [Fact]
    public async Task CookieParameter_IsSentAsCookieHeader()
    {
        const string openApi = """
            openapi: 3.0.1
            info:
              title: Cookie Parameter API
              version: v1
            paths:
              /reports:
                get:
                  operationId: list_reports
                  parameters:
                    - name: session_id
                      in: cookie
                      required: true
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

        using var generatedAssembly = LoadGeneratedAssembly(openApi);
        var clientType = Assert.Single(generatedAssembly.Assembly.GetTypes(), static type => type.Name == "TestClient");
        var handler = new RecordingHttpMessageHandler();
        using var httpClient = new HttpClient(handler)
        {
            BaseAddress = new Uri("https://api.example.com/")
        };

        var client = Activator.CreateInstance(clientType, httpClient);
        Assert.NotNull(client);

        var operationProperty = Assert.Single(clientType.GetProperties(), static property => property.PropertyType.GetMethod("ListReportsAsync") is not null);
        var operationClient = operationProperty.GetValue(client);
        Assert.NotNull(operationClient);

        var operationMethod = operationClient.GetType().GetMethod("ListReportsAsync", BindingFlags.Instance | BindingFlags.Public);
        Assert.NotNull(operationMethod);

        var invocation = Assert.IsAssignableFrom<Task>(operationMethod.Invoke(operationClient, ["abc 123", CancellationToken.None]));
        await invocation;

        var request = Assert.Single(handler.Requests);
        Assert.Equal(["session_id=abc%20123"], Assert.Contains("Cookie", request.Headers));
    }

    [Fact]
    public async Task ArrayParameters_UseOpenApiCompatibleSerialization()
    {
        const string openApi = """
            openapi: 3.0.1
            info:
              title: Array Parameter API
              version: v1
            paths:
              /reports/{ids}:
                get:
                  operationId: list_reports
                  parameters:
                    - name: ids
                      in: path
                      required: true
                      schema:
                        type: array
                        items:
                          type: integer
                    - name: tags
                      in: query
                      required: true
                      schema:
                        type: array
                        items:
                          type: string
                    - name: X-Scope
                      in: header
                      required: true
                      schema:
                        type: array
                        items:
                          type: string
                  responses:
                    '200':
                      description: ok
                      content:
                        application/json:
                          schema:
                            type: object
        """;

        using var generatedAssembly = LoadGeneratedAssembly(openApi);
        var clientType = Assert.Single(generatedAssembly.Assembly.GetTypes(), static type => type.Name == "TestClient");
        var handler = new RecordingHttpMessageHandler();
        using var httpClient = new HttpClient(handler)
        {
            BaseAddress = new Uri("https://api.example.com/")
        };

        var client = Activator.CreateInstance(clientType, httpClient);
        Assert.NotNull(client);

        var operationProperty = Assert.Single(clientType.GetProperties(), static property => property.PropertyType.GetMethod("ListReportsAsync") is not null);
        var operationClient = operationProperty.GetValue(client);
        Assert.NotNull(operationClient);

        var operationMethod = operationClient.GetType().GetMethod("ListReportsAsync", BindingFlags.Instance | BindingFlags.Public);
        Assert.NotNull(operationMethod);

        var invocation = Assert.IsAssignableFrom<Task>(operationMethod.Invoke(operationClient, [new[] { 1, 2 }, new[] { "red", "blue" }, new[] { "read", "write" }, CancellationToken.None]));
        await invocation;

        var request = Assert.Single(handler.Requests);
        Assert.Equal(new Uri("https://api.example.com/reports/1%2C2?tags=red&tags=blue"), request.RequestUri);
        Assert.Equal(["read,write"], Assert.Contains("X-Scope", request.Headers));
    }

    [Fact]
    public async Task IntegerEnumParameter_SendsNumericWireValue()
    {
        const string openApi = """
            openapi: 3.0.1
            info:
              title: Enum Parameter API
              version: v1
            paths:
              /reports:
                get:
                  operationId: list_reports
                  parameters:
                    - name: status
                      in: query
                      required: true
                      schema:
                        $ref: '#/components/schemas/reportStatus'
                    - name: X-Priority
                      in: header
                      required: true
                      schema:
                        $ref: '#/components/schemas/reportStatus'
                  responses:
                    '200':
                      description: ok
                      content:
                        application/json:
                          schema:
                            type: object
            components:
              schemas:
                reportStatus:
                  type: integer
                  enum:
                    - 1
                    - 7
        """;

        using var generatedAssembly = LoadGeneratedAssembly(openApi);
        var clientType = Assert.Single(generatedAssembly.Assembly.GetTypes(), static type => type.Name == "TestClient");
        var enumType = Assert.Single(generatedAssembly.Assembly.GetTypes(), static type => type.Name == "ReportStatus");
        Assert.True(enumType.IsEnum);

        var handler = new RecordingHttpMessageHandler();
        using var httpClient = new HttpClient(handler)
        {
            BaseAddress = new Uri("https://api.example.com/")
        };

        var client = Activator.CreateInstance(clientType, httpClient);
        Assert.NotNull(client);

        var operationProperty = Assert.Single(clientType.GetProperties(), static property => property.PropertyType.GetMethod("ListReportsAsync") is not null);
        var operationClient = operationProperty.GetValue(client);
        Assert.NotNull(operationClient);

        var operationMethod = operationClient.GetType().GetMethod("ListReportsAsync", BindingFlags.Instance | BindingFlags.Public);
        Assert.NotNull(operationMethod);

        var invocation = Assert.IsAssignableFrom<Task>(operationMethod.Invoke(operationClient, [Enum.ToObject(enumType, 7), Enum.ToObject(enumType, 1), CancellationToken.None]));
        await invocation;

        var request = Assert.Single(handler.Requests);
        Assert.Equal(new Uri("https://api.example.com/reports?status=7"), request.RequestUri);
        Assert.Equal(["1"], Assert.Contains("X-Priority", request.Headers));
    }

    [Fact]
    public async Task ParameterNamesWithReservedCharacters_AreUrlEncoded()
    {
        const string openApi = """
            openapi: 3.0.1
            info:
              title: Encoded Name API
              version: v1
            paths:
              /reports:
                get:
                  operationId: list_reports
                  parameters:
                    - name: 'filter value&x'
                      in: query
                      required: true
                      schema:
                        type: string
                    - name: 'session id'
                      in: cookie
                      required: true
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

        using var generatedAssembly = LoadGeneratedAssembly(openApi);
        var clientType = Assert.Single(generatedAssembly.Assembly.GetTypes(), static type => type.Name == "TestClient");
        var handler = new RecordingHttpMessageHandler();
        using var httpClient = new HttpClient(handler)
        {
            BaseAddress = new Uri("https://api.example.com/")
        };

        var client = Activator.CreateInstance(clientType, httpClient);
        Assert.NotNull(client);

        var operationProperty = Assert.Single(clientType.GetProperties(), static property => property.PropertyType.GetMethod("ListReportsAsync") is not null);
        var operationClient = operationProperty.GetValue(client);
        Assert.NotNull(operationClient);

        var operationMethod = operationClient.GetType().GetMethod("ListReportsAsync", BindingFlags.Instance | BindingFlags.Public);
        Assert.NotNull(operationMethod);

        var invocation = Assert.IsAssignableFrom<Task>(operationMethod.Invoke(operationClient, ["abc", "xyz", CancellationToken.None]));
        await invocation;

        var request = Assert.Single(handler.Requests);
        Assert.Equal(new Uri("https://api.example.com/reports?filter%20value%26x=abc"), request.RequestUri);
        Assert.Equal(["session%20id=xyz"], Assert.Contains("Cookie", request.Headers));
    }

    [Fact]
    public void MultipartBinaryPart_IncludesFileName()
    {
        const string openApi = """
            openapi: 3.0.1
            info:
              title: Upload API
              version: v1
            paths:
              /uploads:
                post:
                  operationId: create_upload
                  requestBody:
                    required: true
                    content:
                      multipart/form-data:
                        schema:
                          $ref: '#/components/schemas/uploadParams'
                  responses:
                    '201':
                      description: created
                      content:
                        application/json:
                          schema:
                            type: object
            components:
              schemas:
                uploadParams:
                  type: object
                  required:
                    - file
                  properties:
                    file:
                      type: string
                      format: binary
        """;

        var source = GenerateSource(openApi);

        Assert.Contains("content.Add(new ByteArrayContent(body.File), \"file\", \"file\");", source);
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

        Assert.Contains("public async Task<ListPartnerReportsResponseModel> ListPartnerReportsAsync(int partnerId, int? page = default, CancellationToken cancellationToken = default)", source);
        Assert.Contains("pathBuilder.Append(Uri.EscapeDataString(OpenApiClientHelpers.FormatParameter(partnerId)));", source);
        Assert.Contains("OpenApiClientHelpers.AppendQueryParameter(pathBuilder, ref hasQuery, \"page\", OpenApiClientHelpers.FormatParameter(page));", source);
    }

    [Fact]
    public void OperationLevelParameter_OverridesPathLevelParameter()
    {
        const string openApi = """
            openapi: 3.0.1
            info:
              title: Override Parameters API
              version: v1
            paths:
              /partners/{partner_id}/reports:
                parameters:
                  - name: partner_id
                    in: path
                    required: true
                    schema:
                      type: integer
                  - name: page
                    in: query
                    required: false
                    schema:
                      type: integer
                get:
                  operationId: list_partner_reports
                  parameters:
                    - name: page
                      in: query
                      required: false
                      schema:
                        type: string
                    - name: partner_id
                      in: path
                      required: true
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

        Assert.Contains("public async Task<ListPartnerReportsResponseModel> ListPartnerReportsAsync(string partnerId, string? page = default, CancellationToken cancellationToken = default)", source);
        Assert.DoesNotContain("int partnerId", source);
        Assert.DoesNotContain("int? page = default, string? page = default", source);
        Assert.Contains("OpenApiClientHelpers.AppendQueryParameter(pathBuilder, ref hasQuery, \"page\", OpenApiClientHelpers.FormatParameter(page));", source);
    }

    [Fact]
    public void HeaderParameterOverride_IsCaseInsensitive()
    {
        const string openApi = """
            openapi: 3.0.1
            info:
              title: Header Override API
              version: v1
            paths:
              /reports:
                parameters:
                  - name: X-Request-Id
                    in: header
                    required: false
                    schema:
                      type: integer
                get:
                  operationId: list_reports
                  parameters:
                    - name: x-request-id
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

        Assert.Contains("public async Task<ListReportsResponseModel> ListReportsAsync(string? xRequestId = default, CancellationToken cancellationToken = default)", source);
        Assert.DoesNotContain("int? xRequestId = default", source);
        Assert.Equal(1, source.Split("request.Headers.TryAddWithoutValidation(\"x-request-id\"").Length - 1);
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

    [Fact]
    public void OpenApi32_BasicOperation_GeneratesClient()
    {
        const string openApi = """
            openapi: 3.2.0
            info:
              title: Pet API
              version: v1
            paths:
              /pets:
                get:
                  operationId: list_pets
                  responses:
                    '200':
                      description: ok
                      content:
                        application/json:
                          schema:
                            type: array
                            items:
                              $ref: '#/components/schemas/petResponse'
            components:
              schemas:
                petResponse:
                  type: object
                  required:
                    - id
                    - name
                  properties:
                    id:
                      type: integer
                    name:
                      type: string
        """;

        var source = GenerateSource(openApi);

        Assert.Contains("public async Task<IReadOnlyList<PetResponse>> ListPetsAsync(CancellationToken cancellationToken = default)", source);
        Assert.Contains("public sealed class PetResponse", source);
        Assert.Contains("public required int Id { get; init; }", source);
        Assert.Contains("public required string Name { get; init; }", source);
    }

    [Fact]
    public void OpenApi32_ResponseSummary_IsEmittedAsDocumentation()
    {
        const string openApi = """
            openapi: 3.2.0
            info:
              title: Summary API
              version: v1
            paths:
              /pets/{id}:
                get:
                  operationId: get_pet
                  parameters:
                    - name: id
                      in: path
                      required: true
                      schema:
                        type: integer
                  responses:
                    '200':
                      summary: A pet object.
                      description: The pet identified by the provided id.
                      content:
                        application/json:
                          schema:
                            type: object
        """;

        var source = GenerateSource(openApi);

        Assert.Contains("/// <returns>", source);
        Assert.Contains("/// A pet object.", source);
    }

    [Fact]
    public void OpenApi32_MultipartRequestBody_WorksCorrectly()
    {
        const string openApi = """
            openapi: 3.2.0
            info:
              title: Upload API
              version: v1
            paths:
              /uploads:
                post:
                  operationId: create_upload
                  requestBody:
                    required: true
                    content:
                      multipart/form-data:
                        schema:
                          $ref: '#/components/schemas/uploadParams'
                  responses:
                    '201':
                      description: created
                      content:
                        application/json:
                          schema:
                            type: object
            components:
              schemas:
                uploadParams:
                  type: object
                  required:
                    - file
                  properties:
                    file:
                      type: string
                      format: binary
                    description:
                      type: ["string", "null"]
        """;

        var source = GenerateSource(openApi);

        Assert.Contains("public required byte[] File", source);
        Assert.Contains("public string? Description", source);
        Assert.Contains("var content = new MultipartFormDataContent();", source);
        Assert.Contains("content.Add(new ByteArrayContent(body.File), \"file\", \"file\");", source);
        Assert.Contains("if (body.Description is not null)", source);
        Assert.Contains("content.Add(new StringContent(OpenApiClientHelpers.FormatParameter(body.Description)), \"description\");", source);
    }

    [Fact]
    public void OpenApi32_QueryAndPathParameters_WorkCorrectly()
    {
        const string openApi = """
            openapi: 3.2.0
            info:
              title: Parameter API
              version: v1
            paths:
              /partners/{partner_id}/reports:
                get:
                  operationId: list_partner_reports
                  parameters:
                    - name: partner_id
                      in: path
                      required: true
                      schema:
                        type: integer
                    - name: page
                      in: query
                      required: false
                      schema:
                        type: integer
                    - name: start_date
                      in: query
                      required: true
                      schema:
                        type: string
                        format: date
                  responses:
                    '200':
                      description: ok
                      content:
                        application/json:
                          schema:
                            type: object
        """;

        var source = GenerateSource(openApi);

        Assert.Contains("int partnerId", source);
        Assert.Contains("DateOnly startDate", source);
        Assert.Contains("int? page = default", source);
        Assert.Contains("pathBuilder.Append(Uri.EscapeDataString(OpenApiClientHelpers.FormatParameter(partnerId)));", source);
        Assert.Contains("OpenApiClientHelpers.AppendQueryParameter(pathBuilder, ref hasQuery, \"start_date\", OpenApiClientHelpers.FormatParameter(startDate));", source);
    }

    [Fact]
    public void OpenApi32_ServerUrl_SetsBaseAddress()
    {
        const string openApi = """
            openapi: 3.2.0
            info:
              title: Server URL API
              version: v1
            servers:
              - url: https://api.example.com/v2
                name: production
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

        Assert.Contains("_httpClient.BaseAddress = new Uri(\"https://api.example.com/v2/\", UriKind.Absolute);", source);
    }

    [Fact]
    public void OpenApi32_NullableTypeArrayBodyProperty_GeneratesNullableParameter()
    {
        const string openApi = """
            openapi: 3.2.0
            info:
              title: Nullable Body API
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
                  required:
                    - name
                  properties:
                    name:
                      type: string
                    note:
                      type: ["string", "null"]
                    age:
                      type: ["integer", "null"]
        """;

        var source = GenerateSource(openApi);

        Assert.Contains("public sealed class PartnerCreateParams", source);
        Assert.Contains("public required string Name { get; init; }", source);
        Assert.Contains("public string? Note { get; init; }", source);
        Assert.Contains("public int? Age { get; init; }", source);
    }

    [Fact]
    public void OpenApi32_CustomHttpMethod_GeneratesNewHttpMethod()
    {
        const string openApi = """
            openapi: 3.2.0
            info:
              title: Custom Method API
              version: v1
            paths:
              /resources:
                query:
                  operationId: query_resources
                  responses:
                    '200':
                      description: ok
                      content:
                        application/json:
                          schema:
                            type: object
        """;

        var source = GenerateSource(openApi);

        Assert.Contains("new HttpMethod(\"QUERY\")", source);
        Assert.Contains("QueryResourcesAsync", source);
    }

    [Fact]
    public void MissingOperationId_DerivesMethodNameFromRouteAndVerb()
    {
        const string openApi = """
            openapi: 3.0.1
            info:
              title: No OperationId API
              version: v1
            paths:
              /items:
                get:
                  tags:
                    - items
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

        Assert.Contains("ListAsync(", source);
    }

    [Fact]
    public void CollectionAndSingleResourceGet_WithCapitalizedTag_UseListAndGetMethodNames()
    {
        const string openApi = """
            openapi: 3.0.1
            info:
              title: Partners API
              version: v1
            paths:
              /partners:
                get:
                  operationId: get_partners
                  tags:
                    - Partners
                  responses:
                    '200':
                      description: ok
                      content:
                        application/json:
                          schema:
                            type: object
              /partners/{id}:
                get:
                  operationId: get_partner
                  tags:
                    - Partners
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
                            type: object
        """;

        var source = GenerateSource(openApi);

        Assert.Contains("public async Task<GetPartnersResponseModel> ListAsync(", source);
        Assert.Contains("public async Task<GetPartnerResponseModel> GetAsync(", source);
        Assert.DoesNotContain("GetPartnerAsync", source);
        Assert.DoesNotContain("GetPartnersAsync", source);
    }

    [Fact]
    public void CollectionAndSingleResourceGet_WithMultiWordTag_UseListAndGetMethodNames()
    {
        const string openApi = """
            openapi: 3.0.1
            info:
              title: Account Items API
              version: v1
            paths:
              /account_items:
                get:
                  operationId: get_account_items
                  tags:
                    - Account items
                  responses:
                    '200':
                      description: ok
                      content:
                        application/json:
                          schema:
                            type: object
              /account_items/{id}:
                get:
                  operationId: get_account_item
                  tags:
                    - Account items
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
                            type: object
        """;

        var source = GenerateSource(openApi);

        Assert.Contains("public async Task<GetAccountItemsResponseModel> ListAsync(", source);
        Assert.Contains("public async Task<GetAccountItemResponseModel> GetAsync(", source);
        Assert.DoesNotContain("GetAccountItemAsync", source);
        Assert.DoesNotContain("GetAccountItemsAsync", source);
    }

    [Fact]
    public void CollectionAndSingleResourceGet_WithIePluralTag_UseListAndGetMethodNames()
    {
        const string openApi = """
            openapi: 3.0.1
            info:
              title: Cookies API
              version: v1
            paths:
              /cookies:
                get:
                  operationId: get_cookies
                  tags:
                    - Cookies
                  responses:
                    '200':
                      description: ok
                      content:
                        application/json:
                          schema:
                            type: object
              /cookies/{id}:
                get:
                  operationId: get_cookie
                  tags:
                    - Cookies
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
                            type: object
        """;

        var source = GenerateSource(openApi);

        Assert.Contains("public async Task<GetCookiesResponseModel> ListAsync(", source);
        Assert.Contains("public async Task<GetCookieResponseModel> GetAsync(", source);
        Assert.DoesNotContain("GetCookieAsync", source);
        Assert.DoesNotContain("GetCookiesAsync", source);
    }

    [Fact]
    public void CollectionAndSingleResourceGet_WithShortIePluralTag_UseListAndGetMethodNames()
    {
        const string openApi = """
            openapi: 3.0.1
            info:
              title: Ties API
              version: v1
            paths:
              /ties:
                get:
                  operationId: get_ties
                  tags:
                    - Ties
                  responses:
                    '200':
                      description: ok
                      content:
                        application/json:
                          schema:
                            type: object
              /ties/{id}:
                get:
                  operationId: get_tie
                  tags:
                    - Ties
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
                            type: object
        """;

        var source = GenerateSource(openApi);

        Assert.Contains("public async Task<GetTiesResponseModel> ListAsync(", source);
        Assert.Contains("public async Task<GetTieResponseModel> GetAsync(", source);
        Assert.DoesNotContain("GetTieAsync", source);
        Assert.DoesNotContain("GetTiesAsync", source);
    }

    [Fact]
    public void EmptyOperationId_DerivesMethodNameFromHttpVerb()
    {
        const string openApi = """
            openapi: 3.0.1
            info:
              title: Empty OperationId API
              version: v1
            paths:
              /items:
                post:
                  operationId: ""
                  requestBody:
                    required: true
                    content:
                      application/json:
                        schema:
                          type: object
                  responses:
                    '201':
                      description: created
                      content:
                        application/json:
                          schema:
                            type: object
            """;

        var source = GenerateSource(openApi);

        Assert.Contains("Async(", source);
    }

    [Fact]
    public void ErrorResponse_GeneratesTypedOpenApiException()
    {
        const string openApi = """
            openapi: 3.0.1
            info:
              title: Error API
              version: v1
            paths:
              /partners:
                post:
                  operationId: create_partner
                  responses:
                    '201':
                      description: created
                      content:
                        application/json:
                          schema:
                            type: object
                    '400':
                      description: validation failed
                      content:
                        application/json:
                          schema:
                            $ref: '#/components/schemas/validationProblem'
                    default:
                      description: unexpected error
                      content:
                        text/plain:
                          schema:
                            type: string
            components:
              schemas:
                validationProblem:
                  type: object
                  properties:
                    message:
                      type: string
            """;

        var source = GenerateSource(openApi);

        Assert.Contains("public class OpenApiException : Exception", source);
        Assert.Contains("public class OpenApiException<TError> : OpenApiException", source);
        Assert.Contains("if (!response.IsSuccessStatusCode)", source);
        Assert.Contains("if (OpenApiClientHelpers.ResponseMatchesStatusCode(statusCode, \"400\"))", source);
        Assert.Contains("var error = OpenApiClientHelpers.DeserializeResponseContent<ValidationProblem>(responseContent);", source);
        Assert.Contains("throw new OpenApiException<ValidationProblem>(statusCode, response.ReasonPhrase, contentType, responseContent, error);", source);
        Assert.Contains("throw new OpenApiException<string>(statusCode, response.ReasonPhrase, contentType, responseContent, responseContent);", source);
        Assert.DoesNotContain("response.EnsureSuccessStatusCode();", source);
    }

    [Fact]
    public void ProblemJsonErrorResponse_UsesDeclaredOpenApiSchema()
    {
        const string openApi = """
            openapi: 3.0.1
            info:
              title: Problem API
              version: v1
            paths:
              /partners:
                get:
                  operationId: list_partners
                  responses:
                    '200':
                      description: ok
                      content:
                        application/json:
                          schema:
                            type: object
                    '422':
                      description: invalid request
                      content:
                        application/problem+json:
                          schema:
                            $ref: '#/components/schemas/problemDetails'
            components:
              schemas:
                problemDetails:
                  type: object
                  properties:
                    title:
                      type: string
                    detail:
                      type: string
            """;

        var source = GenerateSource(openApi);

        Assert.Contains("public sealed class ProblemDetails", source);
        Assert.DoesNotContain("public sealed class OpenApiProblemDetails", source);
        Assert.Contains("var error = OpenApiClientHelpers.DeserializeResponseContent<ProblemDetails>(responseContent);", source);
        Assert.Contains("throw new OpenApiException<ProblemDetails>(statusCode, response.ReasonPhrase, contentType, responseContent, error);", source);
    }

    [Fact]
    public void NullableReferencedErrorResponse_UsesNullablePayloadTypeWithoutLosingTypedException()
    {
        const string openApi = """
            openapi: 3.0.1
            info:
              title: Nullable Error API
              version: v1
            paths:
              /partners:
                get:
                  operationId: list_partners
                  responses:
                    '200':
                      description: ok
                      content:
                        application/json:
                          schema:
                            type: object
                    '422':
                      description: invalid request
                      content:
                        application/problem+json:
                          schema:
                            $ref: '#/components/schemas/problemDetails'
            components:
              schemas:
                problemDetails:
                  nullable: true
                  type: object
                  properties:
                    title:
                      type: string
                    detail:
                      type: string
            """;

        var source = GenerateSource(openApi);

        Assert.Contains("var error = OpenApiClientHelpers.DeserializeResponseContent<ProblemDetails>(responseContent);", source);
        Assert.Contains("throw new OpenApiException<ProblemDetails>(statusCode, response.ReasonPhrase, contentType, responseContent, error);", source);
    }
}

sealed file class TestHttpServer : IAsyncDisposable
{
    private readonly TcpListener _listener;
    private readonly Task<string> _requestTargetTask;

    private TestHttpServer(TcpListener listener, Uri baseAddress, Task<string> requestTargetTask)
    {
        _listener = listener;
        BaseAddress = baseAddress;
        _requestTargetTask = requestTargetTask;
    }

    public Uri BaseAddress { get; }

    public static TestHttpServer Start()
    {
        var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();

        var endpoint = Assert.IsType<IPEndPoint>(listener.LocalEndpoint);
        var baseAddress = new Uri($"http://127.0.0.1:{endpoint.Port}/api/v1");
        var requestTargetTask = AcceptSingleRequestAsync(listener);
        return new TestHttpServer(listener, baseAddress, requestTargetTask);
    }

    public Task<string> GetRequestTargetAsync() => _requestTargetTask;

    public ValueTask DisposeAsync()
    {
        _listener.Stop();
        return ValueTask.CompletedTask;
    }

    private static async Task<string> AcceptSingleRequestAsync(TcpListener listener)
    {
        using var client = await listener.AcceptTcpClientAsync();
        await using var stream = client.GetStream();
        using var reader = new StreamReader(stream, Encoding.ASCII, detectEncodingFromByteOrderMarks: false, leaveOpen: true);

        var requestLine = await reader.ReadLineAsync();
        Assert.False(string.IsNullOrEmpty(requestLine));

        string? headerLine;
        do
        {
            headerLine = await reader.ReadLineAsync();
        }
        while (!string.IsNullOrEmpty(headerLine));

        const string responseBody = "{}";
        var response = $"HTTP/1.1 200 OK\r\nContent-Type: application/json\r\nContent-Length: {responseBody.Length}\r\nConnection: close\r\n\r\n{responseBody}";
        var responseBytes = Encoding.ASCII.GetBytes(response);
        await stream.WriteAsync(responseBytes);

        var parts = requestLine.Split(' ');
        Assert.True(parts.Length >= 2, $"Unexpected request line: {requestLine}");
        return parts[1];
    }
}
