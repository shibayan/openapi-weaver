using Xunit;

namespace OpenApiClientGenerator.Tests;

public sealed partial class OpenApiClientSourceGeneratorTests
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
}
