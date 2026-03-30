using Xunit;

namespace OpenApiWeaver.Tests;

public sealed partial class OpenApiWeaverSourceGeneratorTests
{
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
    public void IntegerEnumSchema_GeneratesCSharpEnumType()
    {
        const string openApi = """
            openapi: 3.0.1
            info:
              title: Enum API
              version: v1
            paths: {}
            components:
              schemas:
                orderState:
                  type: integer
                  enum:
                    - 0
                    - 1
                    - 2
            """;

        var source = GenerateSource(openApi);

        Assert.Contains("public enum OrderState", source);
        Assert.Contains("Value0 = 0,", source);
        Assert.Contains("Value1 = 1,", source);
        Assert.Contains("Value2 = 2", source);
    }

    [Fact]
    public void InlineEnumProperty_GeneratesNamedSchemaType()
    {
        const string openApi = """
            openapi: 3.0.1
            info:
              title: Inline Enum API
              version: v1
            paths: {}
            components:
              schemas:
                orderResponse:
                  type: object
                  required:
                    - status
                  properties:
                    status:
                      type: string
                      enum:
                        - pending
                        - completed
            """;

        var source = GenerateSource(openApi);

        Assert.Contains("public required OrderResponse.StatusEnum Status { get; init; }", source);
        Assert.Contains("public readonly record struct StatusEnum(string Value)", source);
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
    public void AllOfSchema_FlattensReferencedAndInlineProperties()
    {
        const string openApi = """
            openapi: 3.2.0
            info:
              title: Composition API
              version: v1
            paths: {}
            components:
              schemas:
                partnerBase:
                  type: object
                  required:
                    - company_id
                  properties:
                    company_id:
                      type: integer
                partnerDetail:
                  allOf:
                    - $ref: '#/components/schemas/partnerBase'
                    - type: object
                      required:
                        - display_name
                      properties:
                        display_name:
                          type: string
            """;

        var source = GenerateSource(openApi);

        Assert.Contains("public sealed class PartnerDetail", source);
        Assert.Contains("public required int CompanyId { get; init; }", source);
        Assert.Contains("public required string DisplayName { get; init; }", source);
    }

    [Fact]
    public void AdditionalPropertiesSchema_GeneratesDictionaryBackedType()
    {
        const string openApi = """
            openapi: 3.2.0
            info:
              title: Dictionary API
              version: v1
            paths:
              /labels:
                get:
                  operationId: list_labels
                  responses:
                    '200':
                      description: ok
                      content:
                        application/json:
                          schema:
                            $ref: '#/components/schemas/labelMap'
            components:
              schemas:
                labelMap:
                  type: object
                  additionalProperties:
                    type: string
            """;

        var source = GenerateSource(openApi);

        Assert.Contains("public sealed class LabelMap : Dictionary<string, string>", source);
        Assert.Contains("public async Task<LabelMap> ListLabelsAsync(CancellationToken cancellationToken = default)", source);
    }

    [Fact]
    public void InlineArrayObjectItems_GenerateNamedSchemaTypes()
    {
        const string openApi = """
            openapi: 3.2.0
            info:
              title: Inline Object API
              version: v1
            paths: {}
            components:
              schemas:
                companyIndexResponse:
                  type: object
                  required:
                    - companies
                  properties:
                    companies:
                      type: array
                      items:
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

        Assert.Contains("public required IReadOnlyList<CompanyIndexResponse.CompaniesItem> Companies { get; init; }", source);
        Assert.Contains("public sealed class CompaniesItem", source);
        Assert.Contains("public required int Id { get; init; }", source);
        Assert.Contains("public required string Name { get; init; }", source);
    }

    [Fact]
    public void InlineObjectProperty_GeneratesNestedModelType()
    {
        const string openApi = """
            openapi: 3.2.0
            info:
              title: Inline Property API
              version: v1
            paths: {}
            components:
              schemas:
                orderResponse:
                  type: object
                  required:
                    - metadata
                  properties:
                    metadata:
                      type: object
                      required:
                        - request_id
                      properties:
                        request_id:
                          type: string
            """;

        var source = GenerateSource(openApi);

        Assert.Contains("public required OrderResponse.MetadataModel Metadata { get; init; }", source);
        Assert.Contains("public sealed class MetadataModel", source);
        Assert.Contains("public required string RequestId { get; init; }", source);
    }

    [Fact]
    public void NestedInlineArrayObjectItems_GenerateMultiLevelNestedTypeNames()
    {
        const string openApi = """
            openapi: 3.2.0
            info:
              title: Nested Inline API
              version: v1
            paths: {}
            components:
              schemas:
                orderResponse:
                  type: object
                  required:
                    - shipping
                  properties:
                    shipping:
                      type: object
                      required:
                        - packages
                      properties:
                        packages:
                          type: array
                          items:
                            type: object
                            required:
                              - tracking_number
                            properties:
                              tracking_number:
                                type: string
            """;

        var source = GenerateSource(openApi);

        Assert.Contains("public required OrderResponse.ShippingModel Shipping { get; init; }", source);
        Assert.Contains("public required IReadOnlyList<OrderResponse.ShippingModel.PackagesItem> Packages { get; init; }", source);
        Assert.Contains("public sealed class ShippingModel", source);
        Assert.Contains("public sealed class PackagesItem", source);
        Assert.Contains("public required string TrackingNumber { get; init; }", source);
    }

    [Fact]
    public void InlineSchemaTitle_DoesNotOverrideNestedGeneratedTypeName()
    {
        const string openApi = """
            openapi: 3.2.0
            info:
              title: Inline Title API
              version: v1
            paths: {}
            components:
              schemas:
                companyIndexResponse:
                  type: object
                  required:
                    - companies
                  properties:
                    companies:
                      type: array
                      items:
                        title: CompanySummary
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

        Assert.Contains("public required IReadOnlyList<CompanyIndexResponse.CompaniesItem> Companies { get; init; }", source);
        Assert.Contains("public sealed class CompaniesItem", source);
        Assert.DoesNotContain("public sealed class CompanySummary", source);
    }

    [Fact]
    public void IdenticalInlineObjectItems_GenerateDistinctSchemaTypeDefinitions()
    {
        const string openApi = """
            openapi: 3.2.0
            info:
              title: Shared Inline Object API
              version: v1
            paths: {}
            components:
              schemas:
                companyIndexResponse:
                  type: object
                  required:
                    - companies
                  properties:
                    companies:
                      type: array
                      items:
                        type: object
                        required:
                          - id
                          - name
                        properties:
                          id:
                            type: integer
                          name:
                            type: string
                tagIndexResponse:
                  type: object
                  required:
                    - tags
                  properties:
                    tags:
                      type: array
                      items:
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

        Assert.Contains("public required IReadOnlyList<CompanyIndexResponse.CompaniesItem> Companies { get; init; }", source);
        Assert.Contains("public required IReadOnlyList<TagIndexResponse.TagsItem> Tags { get; init; }", source);
        Assert.Equal(1, source.Split("public sealed class CompaniesItem").Length - 1);
        Assert.Contains("public sealed class TagsItem", source);
    }

    [Fact]
    public void NullableOneOfReferenceResponse_GeneratesNullableReferencedType()
    {
        const string openApi = """
            openapi: 3.2.0
            info:
              title: Nullable Union API
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
                            oneOf:
                              - $ref: '#/components/schemas/partnerResponse'
                              - type: 'null'
            components:
              schemas:
                partnerResponse:
                  type: object
                  required:
                    - company_id
                  properties:
                    company_id:
                      type: integer
            """;

        var source = GenerateSource(openApi);

        Assert.Contains("public async Task<PartnerResponse?> GetPartnerAsync(int id, CancellationToken cancellationToken = default)", source);
        Assert.Contains("return await response.Content.ReadFromJsonAsync<PartnerResponse?>", source);
        Assert.DoesNotContain("The response body was empty.", source);
    }

    [Fact]
    public void PatternPropertiesSchema_GeneratesDictionaryBackedType()
    {
        const string openApi = """
            openapi: 3.2.0
            info:
              title: Pattern Properties API
              version: v1
            paths: {}
            components:
              schemas:
                scoreMap:
                  type: object
                  patternProperties:
                    '^score_[a-z]+$':
                      type: integer
            """;

        var source = GenerateSource(openApi);

        Assert.Contains("public sealed class ScoreMap : Dictionary<string, int>", source);
    }

    [Fact]
    public void PropertiesAndAdditionalPropertiesSchema_GeneratesDictionaryTypeWithProperties()
    {
        const string openApi = """
            openapi: 3.2.0
            info:
              title: Hybrid Object API
              version: v1
            paths: {}
            components:
              schemas:
                metadataBag:
                  type: object
                  required:
                    - id
                  properties:
                    id:
                      type: string
                  additionalProperties:
                    type: string
            """;

        var source = GenerateSource(openApi);

        Assert.Contains("public sealed class MetadataBag : Dictionary<string, string>", source);
        Assert.Contains("public required string Id { get; init; }", source);
    }

    [Fact]
    public void NullableAnyOfPrimitiveResponse_GeneratesNullablePrimitiveType()
    {
        const string openApi = """
            openapi: 3.2.0
            info:
              title: Nullable Primitive API
              version: v1
            paths:
              /count:
                get:
                  operationId: get_count
                  responses:
                    '200':
                      description: ok
                      content:
                        application/json:
                          schema:
                            anyOf:
                              - type: integer
                              - type: 'null'
            """;

        var source = GenerateSource(openApi);

        Assert.Contains("public async Task<int?> GetCountAsync(CancellationToken cancellationToken = default)", source);
        Assert.Contains("return await response.Content.ReadFromJsonAsync<int?>", source);
    }

    [Fact]
    public void MixedDictionaryValueTypes_FallbackToJsonElement()
    {
        const string openApi = """
            openapi: 3.2.0
            info:
              title: Mixed Dictionary API
              version: v1
            paths: {}
            components:
              schemas:
                flexibleMap:
                  type: object
                  additionalProperties:
                    type: string
                  patternProperties:
                    '^score_[a-z]+$':
                      type: integer
            """;

        var source = GenerateSource(openApi);

        Assert.Contains("public sealed class FlexibleMap : Dictionary<string, JsonElement>", source);
    }

    [Fact]
    public void SchemaDescriptions_AreEmittedAsXmlDocumentationComments()
    {
        const string openApi = """
            openapi: 3.2.0
            info:
              title: Schema Docs API
              version: v1
            paths: {}
            components:
              schemas:
                partnerResponse:
                  title: Partner payload
                  description: Represents a partner in API responses.
                  type: object
                  required:
                    - company_id
                  properties:
                    company_id:
                      title: Company identifier
                      description: The owning company identifier.
                      type: integer
            """;

        var source = GenerateSource(openApi);

        Assert.Contains("/// Partner payload", source);
        Assert.Contains("/// Represents a partner in API responses.", source);
        Assert.Contains("/// Company identifier", source);
        Assert.Contains("/// The owning company identifier.", source);
        Assert.Contains("public required int CompanyId { get; init; }", source);
    }

    [Fact]
    public void SchemaDocumentation_StripsHtmlTags()
    {
        const string openApi = """
            openapi: 3.2.0
            info:
              title: Schema Docs API
              version: v1
            paths: {}
            components:
              schemas:
                partnerResponse:
                  title: <strong>Partner</strong> payload
                  description: <p>Represents a <em>partner</em> in API responses.</p>
                  type: object
                  required:
                    - company_id
                  properties:
                    company_id:
                      title: Company <code>identifier</code>
                      description: <div>The owning company identifier.</div>
                      type: integer
            """;

        var source = GenerateSource(openApi);

        Assert.Contains("/// Partner payload", source);
        Assert.Contains("/// Represents a partner in API responses.", source);
        Assert.Contains("/// Company identifier", source);
        Assert.Contains("/// The owning company identifier.", source);
        Assert.DoesNotContain("<strong>", source);
        Assert.DoesNotContain("<em>", source);
        Assert.DoesNotContain("<code>", source);
        Assert.DoesNotContain("<div>", source);
    }

    [Fact]
    public void NullableTypeArray_GeneratesNullableProperty()
    {
        const string openApi = """
            openapi: 3.2.0
            info:
              title: Nullable Type Array API
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
                      type: ["string", "null"]
                    note:
                      type:
                        - string
                        - "null"
            """;

        var source = GenerateSource(openApi);

        Assert.Contains("public required int CompanyId { get; init; }", source);
        Assert.Contains("public string? DisplayName { get; init; }", source);
        Assert.Contains("public string? Note { get; init; }", source);
    }

    [Fact]
    public void NullableTypeArrayInteger_GeneratesNullableIntProperty()
    {
        const string openApi = """
            openapi: 3.2.0
            info:
              title: Nullable Int API
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
                    optional_count:
                      type: ["integer", "null"]
                    optional_amount:
                      type: ["number", "null"]
                    optional_flag:
                      type: ["boolean", "null"]
            """;

        var source = GenerateSource(openApi);

        Assert.Contains("public required int CompanyId { get; init; }", source);
        Assert.Contains("public int? OptionalCount { get; init; }", source);
        Assert.Contains("public decimal? OptionalAmount { get; init; }", source);
        Assert.Contains("public bool? OptionalFlag { get; init; }", source);
    }

    [Fact]
    public void NumberFormat_GeneratesConfiguredNumericTypes()
    {
        const string openApi = """
            openapi: 3.2.0
            info:
              title: Number Format API
              version: v1
            paths: {}
            components:
              schemas:
                metricsResponse:
                  type: object
                  properties:
                    amount:
                      type: number
                    ratio:
                      type: number
                      format: float
                    score:
                      type: number
                      format: double
                    price:
                      type: number
                      format: decimal
            """;

        var source = GenerateSource(openApi);

        Assert.Contains("public decimal? Amount { get; init; }", source);
        Assert.Contains("public float? Ratio { get; init; }", source);
        Assert.Contains("public double? Score { get; init; }", source);
        Assert.Contains("public decimal? Price { get; init; }", source);
    }

    [Fact]
    public void NullableTypeArrayResponse_GeneratesNullableReturnType()
    {
        const string openApi = """
            openapi: 3.2.0
            info:
              title: Nullable Type Array Response API
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
                            type: ["object", "null"]
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
    public void RequiredNullableTypeArrayProperty_GeneratesRequiredNullableProperty()
    {
        const string openApi = """
            openapi: 3.2.0
            info:
              title: Required Nullable API
              version: v1
            paths: {}
            components:
              schemas:
                partnerResponse:
                  type: object
                  required:
                    - company_id
                    - display_name
                  properties:
                    company_id:
                      type: integer
                    display_name:
                      type: ["string", "null"]
            """;

        var source = GenerateSource(openApi);

        Assert.Contains("public required int CompanyId { get; init; }", source);
        Assert.Contains("public required string? DisplayName { get; init; }", source);
    }

    [Fact]
    public void ConsecutiveUppercaseAbbreviation_SplitsOnCaseBoundary()
    {
        const string openApi = """
            openapi: 3.0.1
            info:
              title: Abbreviation API
              version: v1
            paths: {}
            components:
              schemas:
                HTTPResponse:
                  type: object
                  required:
                    - statusCode
                  properties:
                    statusCode:
                      type: integer
                    responseURL:
                      type: string
            """;

        var source = GenerateSource(openApi);

        Assert.Contains("public sealed class HttpResponse", source);
        Assert.Contains("public required int StatusCode { get; init; }", source);
        Assert.Contains("public string? ResponseUrl { get; init; }", source);
    }

    [Fact]
    public void NonAsciiPropertyNames_GeneratesValidIdentifiers()
    {
        const string openApi = """
            openapi: 3.0.1
            info:
              title: Non-ASCII API
              version: v1
            paths: {}
            components:
              schemas:
                companyResponse:
                  type: object
                  required:
                    - company_name
                  properties:
                    company_name:
                      type: string
                    note:
                      type: string
            """;

        var source = GenerateSource(openApi);

        Assert.Contains("public sealed class CompanyResponse", source);
        Assert.Contains("[JsonPropertyName(\"company_name\")]", source);
        Assert.Contains("public required string CompanyName { get; init; }", source);
    }

    [Fact]
    public void CircularAllOfReference_DoesNotCauseInfiniteLoop()
    {
        const string openApi = """
            openapi: 3.0.1
            info:
              title: Circular API
              version: v1
            paths: {}
            components:
              schemas:
                nodeA:
                  allOf:
                    - $ref: '#/components/schemas/nodeB'
                    - type: object
                      properties:
                        name:
                          type: string
                nodeB:
                  allOf:
                    - $ref: '#/components/schemas/nodeA'
                    - type: object
                      properties:
                        value:
                          type: integer
            """;

        var source = GenerateSource(openApi);

        Assert.Contains("public sealed class NodeA", source);
        Assert.Contains("public sealed class NodeB", source);
    }

    [Fact]
    public void SelfReferencingSchema_GeneratesValidType()
    {
        const string openApi = """
            openapi: 3.0.1
            info:
              title: Tree API
              version: v1
            paths: {}
            components:
              schemas:
                treeNode:
                  type: object
                  required:
                    - name
                  properties:
                    name:
                      type: string
                    children:
                      type: array
                      items:
                        $ref: '#/components/schemas/treeNode'
            """;

        var source = GenerateSource(openApi);

        Assert.Contains("public sealed class TreeNode", source);
        Assert.Contains("public required string Name { get; init; }", source);
        Assert.Contains("public IReadOnlyList<TreeNode>? Children { get; init; }", source);
    }

    [Fact]
    public void ExtremelyLongSchemaName_GeneratesValidIdentifier()
    {
        const string openApi = """
            openapi: 3.0.1
            info:
              title: Long Schema API
              version: v1
            paths: {}
            components:
              schemas:
                this_is_an_extremely_long_schema_name_that_should_still_work_correctly:
                  type: object
                  required:
                    - id
                  properties:
                    id:
                      type: integer
            """;

        var source = GenerateSource(openApi);

        Assert.Contains("public sealed class ThisIsAnExtremelyLongSchemaNameThatShouldStillWorkCorrectly", source);
        Assert.Contains("public required int Id { get; init; }", source);
    }

    [Fact]
    public void ReservedWordPropertyName_GeneratesSafeIdentifier()
    {
        const string openApi = """
            openapi: 3.0.1
            info:
              title: Reserved Word API
              version: v1
            paths: {}
            components:
              schemas:
                filterResponse:
                  type: object
                  required:
                    - class
                  properties:
                    class:
                      type: string
                    default:
                      type: boolean
            """;

        var source = GenerateSource(openApi);

        Assert.Contains("[JsonPropertyName(\"class\")]", source);
        Assert.Contains("public required string Class { get; init; }", source);
        Assert.Contains("[JsonPropertyName(\"default\")]", source);
    }
}
