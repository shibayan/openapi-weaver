using System.Text.Json;

using Xunit;

namespace OpenApiWeaver.Tests;

public sealed partial class ClientGeneratorTests
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
    public void SchemaNamesThatNormalizeToSameIdentifier_GenerateUniqueTypeNames()
    {
        const string openApi = """
            openapi: 3.0.1
            info:
              title: Naming API
              version: v1
            paths: {}
            components:
              schemas:
                user-id:
                  type: object
                  properties:
                    id:
                      type: integer
                user_id:
                  type: object
                  properties:
                    id:
                      type: integer
        """;

        var source = GenerateSource(openApi);

        Assert.Contains("public sealed class UserId", source);
        Assert.Contains("public sealed class UserId2", source);
    }

    [Fact]
    public void SchemaPropertiesThatNormalizeToSameIdentifier_GenerateUniquePropertyNames()
    {
        const string openApi = """
            openapi: 3.0.1
            info:
              title: Naming API
              version: v1
            paths: {}
            components:
              schemas:
                userResponse:
                  type: object
                  properties:
                    user-id:
                      type: integer
                    user_id:
                      type: integer
        """;

        var source = GenerateSource(openApi);

        Assert.Contains("[JsonPropertyName(\"user-id\")]", source);
        Assert.Contains("public int? UserId { get; init; }", source);
        Assert.Contains("[JsonPropertyName(\"user_id\")]", source);
        Assert.Contains("public int? UserId2 { get; init; }", source);
    }

    [Fact]
    public void SerializerOptionsHelperName_DoesNotRenameComponentSchema()
    {
        const string openApi = """
            openapi: 3.0.1
            info:
              title: Naming API
              version: v1
            paths: {}
            components:
              schemas:
                testClientJsonSerializerOptions:
                  type: object
                  properties:
                    id:
                      type: integer
            """;

        var source = GenerateSource(openApi);

        Assert.Contains("public sealed class TestClientJsonSerializerOptions", source);
        Assert.DoesNotContain("internal static class TestClientJsonSerializerOptions", source);
        Assert.DoesNotContain("public sealed class TestClientJsonSerializerOptions2", source);
    }

    [Fact]
    public void SerializerOptionsHelperName_IsSuffixedWhenDirectionalSchemaCollides()
    {
        const string openApi = """
            openapi: 3.0.1
            info:
              title: Naming API
              version: v1
            paths: {}
            components:
              schemas:
                testClientJsonSerializerOptions:
                  type: object
                  properties:
                    id:
                      type: integer
                      readOnly: true
            """;

        var source = GenerateSource(openApi);

        Assert.Contains("public sealed class TestClientJsonSerializerOptions", source);
        Assert.Contains("internal static class TestClientJsonSerializerOptions2", source);
        Assert.DoesNotContain("public sealed class TestClientJsonSerializerOptions2", source);
    }

    [Fact]
    public void DirectionalSerializerMetadata_UsesQualifiedMetadataTypes()
    {
        const string openApi = """
            openapi: 3.0.1
            info:
              title: Metadata Collision API
              version: v1
            paths: {}
            components:
              schemas:
                jsonTypeInfo:
                  type: object
                  properties:
                    id:
                      type: integer
                      readOnly: true
                defaultJsonTypeInfoResolver:
                  type: object
                  properties:
                    name:
                      type: string
            """;

        var source = GenerateSource(openApi);

        Assert.Contains("public sealed class JsonTypeInfo", source);
        Assert.Contains("public sealed class DefaultJsonTypeInfoResolver", source);
        Assert.Contains("System.Text.Json.Serialization.Metadata.JsonTypeInfo typeInfo", source);
        Assert.Contains("new System.Text.Json.Serialization.Metadata.DefaultJsonTypeInfoResolver", source);
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
    public void EnumSchema_CollidingMemberNames_GeneratesUniqueMembers()
    {
        const string openApi = """
            openapi: 3.0.1
            info:
              title: Enum API
              version: v1
            paths: {}
            components:
              schemas:
                conflictStatus:
                  type: string
                  enum:
                    - pending2
                    - pending
                    - Pending
            """;

        var source = GenerateSource(openApi);

        Assert.Contains("public static readonly ConflictStatus Pending2 = new(\"pending2\");", source);
        Assert.Contains("public static readonly ConflictStatus Pending = new(\"pending\");", source);
        Assert.Contains("public static readonly ConflictStatus Pending3 = new(\"Pending\");", source);
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
        Assert.Contains("?? throw new OpenApiException((int)response.StatusCode, response.ReasonPhrase, response.Content?.Headers?.ContentType?.MediaType, null);", source);
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
    public void AllOfSchema_RequiredOnComposedSchema_AppliesToReferencedProperties()
    {
        const string openApi = """
            openapi: 3.2.0
            info:
              title: Composition API
              version: v1
            paths: {}
            components:
              schemas:
                pet:
                  type: object
                  properties:
                    name:
                      type: string
                    tag:
                      type: string
                newPet:
                  allOf:
                    - $ref: '#/components/schemas/pet'
                  required:
                    - name
            """;

        var source = GenerateSource(openApi);

        Assert.Contains("public sealed class NewPet", source);
        Assert.Contains("public required string Name { get; init; }", source);
        Assert.Contains("public string? Tag { get; init; }", source);
    }

    [Fact]
    public void NestedDictionaryProperty_UsesInlineValueModel()
    {
        const string openApi = """
            openapi: 3.2.0
            info:
              title: Nested Dictionary API
              version: v1
            paths: {}
            components:
              schemas:
                resource:
                  type: object
                  properties:
                    labels:
                      type: object
                      additionalProperties:
                        type: object
                        properties:
                          color:
                            type: string
            """;

        var source = GenerateSource(openApi);

        Assert.Contains("public IReadOnlyDictionary<string, Resource.LabelsValue>? Labels { get; init; }", source);
        Assert.Contains("public sealed class LabelsValue", source);
        Assert.DoesNotContain("IReadOnlyDictionary<string, JsonElement>", source);
    }

    [Fact]
    public void DictionaryConverter_WithOptionalProperty_DoesNotEmitUnusedVariableWarnings()
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
                    display_name:
                      type: string
                  additionalProperties:
                    type: string
            """;

        var result = RunGenerator(openApi);

        AssertNoErrorDiagnostics(result);
        Assert.DoesNotContain(
            result.Compilation.GetDiagnostics(TestContext.Current.CancellationToken),
            static diagnostic => string.Equals(diagnostic.Id, "CS0219", StringComparison.Ordinal));
    }

    [Fact]
    public void EnumValuesAndDescriptions_WithUnicodeLineSeparators_GenerateValidSource()
    {
        const string openApi = """
            openapi: 3.0.1
            info:
              title: Unicode API
              version: v1
            paths: {}
            components:
              schemas:
                orderKind:
                  type: string
                  description: "line1\u2028line2"
                  enum:
                    - "alpha\u2028beta"
            """;

        var source = GenerateSource(openApi);

        Assert.Contains("new(\"alpha\\u2028beta\")", source);
        Assert.Contains("/// line1", source);
        Assert.Contains("/// line2", source);
    }

    [Fact]
    public void InlineSchemaNameCollidingWithParentType_GetsUniqueName()
    {
        const string openApi = """
            openapi: 3.0.1
            info:
              title: Error Envelope API
              version: v1
            paths: {}
            components:
              schemas:
                errorModel:
                  type: object
                  properties:
                    error:
                      type: object
                      properties:
                        message:
                          type: string
            """;

        var source = GenerateSource(openApi);

        Assert.Contains("public sealed class ErrorModel2", source);
        Assert.Contains("public ErrorModel.ErrorModel2? Error { get; init; }", source);
    }

    [Fact]
    public void DiscriminatorSchema_GeneratesPolymorphicBaseAndDerivedTypes()
    {
        const string openApi = """
            openapi: 3.2.0
            info:
              title: Polymorphism API
              version: v1
            paths: {}
            components:
              schemas:
                animal:
                  type: object
                  discriminator:
                    propertyName: kind
                    mapping:
                      dog: '#/components/schemas/dog'
                      cat: '#/components/schemas/cat'
                  oneOf:
                    - $ref: '#/components/schemas/dog'
                    - $ref: '#/components/schemas/cat'
                  required:
                    - kind
                    - name
                  properties:
                    kind:
                      type: string
                    name:
                      type: string
                dog:
                  allOf:
                    - $ref: '#/components/schemas/animal'
                    - type: object
                      required:
                        - kind
                        - barks
                      properties:
                        kind:
                          type: string
                        barks:
                          type: boolean
                cat:
                  allOf:
                    - $ref: '#/components/schemas/animal'
                    - type: object
                      required:
                        - kind
                        - lives
                      properties:
                        kind:
                          type: string
                        lives:
                          type: integer
            """;

        var source = GenerateSource(openApi);

        Assert.Contains("[JsonPolymorphic(TypeDiscriminatorPropertyName = \"kind\")]", source);
        Assert.Contains("[JsonDerivedType(typeof(Dog), typeDiscriminator: \"dog\")]", source);
        Assert.Contains("[JsonDerivedType(typeof(Cat), typeDiscriminator: \"cat\")]", source);
        Assert.Contains("public class Animal", source);
        Assert.Contains("public sealed class Dog : Animal", source);
        Assert.Contains("public sealed class Cat : Animal", source);
        Assert.Contains("public required string Name { get; init; }", source);
        Assert.Contains("public required bool Barks { get; init; }", source);
        Assert.Contains("public required int Lives { get; init; }", source);
        Assert.DoesNotContain("[JsonPropertyName(\"kind\")]", source);
        Assert.Equal(1, source.Split("public required string Name { get; init; }").Length - 1);
    }

    [Fact]
    public void DiscriminatorSchema_WithoutMapping_UsesReferencedSchemaNames()
    {
        const string openApi = """
            openapi: 3.2.0
            info:
              title: Polymorphism API
              version: v1
            paths: {}
            components:
              schemas:
                vehicle:
                  type: object
                  discriminator:
                    propertyName: vehicle_type
                  oneOf:
                    - $ref: '#/components/schemas/car'
                    - $ref: '#/components/schemas/truck'
                car:
                  allOf:
                    - $ref: '#/components/schemas/vehicle'
                    - type: object
                      properties:
                        seat_count:
                          type: integer
                truck:
                  allOf:
                    - $ref: '#/components/schemas/vehicle'
                    - type: object
                      properties:
                        payload_kg:
                          type: integer
            """;

        var source = GenerateSource(openApi);

        Assert.Contains("[JsonDerivedType(typeof(Car), typeDiscriminator: \"car\")]", source);
        Assert.Contains("[JsonDerivedType(typeof(Truck), typeDiscriminator: \"truck\")]", source);
    }

    [Fact]
    public void DiscriminatorSchema_WithPartialMapping_UsesMappingValueAndSchemaNameFallback()
    {
        const string openApi = """
            openapi: 3.2.0
            info:
              title: Polymorphism API
              version: v1
            paths: {}
            components:
              schemas:
                shape:
                  type: object
                  discriminator:
                    propertyName: kind
                    mapping:
                      round: '#/components/schemas/circle'
                  oneOf:
                    - $ref: '#/components/schemas/circle'
                    - $ref: '#/components/schemas/square'
                circle:
                  allOf:
                    - $ref: '#/components/schemas/shape'
                    - type: object
                      properties:
                        radius:
                          type: number
                square:
                  allOf:
                    - $ref: '#/components/schemas/shape'
                    - type: object
                      properties:
                        side:
                          type: number
            """;

        var source = GenerateSource(openApi);

        Assert.Contains("[JsonDerivedType(typeof(Circle), typeDiscriminator: \"round\")]", source);
        Assert.Contains("[JsonDerivedType(typeof(Square), typeDiscriminator: \"square\")]", source);
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
    public void PropertiesAndAdditionalPropertiesSchema_SerializesDeclaredPropertiesAndDictionaryEntries()
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
                    display_name:
                      type: string
                  additionalProperties:
                    type: string
            """;

        using var assembly = LoadGeneratedAssembly(openApi);
        var metadataBagType = Assert.Single(assembly.Assembly.GetTypes(), static type => type.Name == "MetadataBag");
        var options = new JsonSerializerOptions(JsonSerializerDefaults.Web);

        var metadataBag = JsonSerializer.Deserialize(
            """{"id":"known","display_name":"Label","extra":"value"}""",
            metadataBagType,
            options);
        Assert.NotNull(metadataBag);

        Assert.Equal("known", metadataBagType.GetProperty("Id")?.GetValue(metadataBag));
        Assert.Equal("Label", metadataBagType.GetProperty("DisplayName")?.GetValue(metadataBag));

        var dictionary = Assert.IsAssignableFrom<IDictionary<string, string>>(metadataBag);
        Assert.False(dictionary.ContainsKey("id"));
        Assert.Equal("value", dictionary["extra"]);

        var serialized = JsonSerializer.Serialize(metadataBag, metadataBagType, options);
        Assert.Contains("\"id\":\"known\"", serialized);
        Assert.Contains("\"display_name\":\"Label\"", serialized);
        Assert.Contains("\"extra\":\"value\"", serialized);
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
        Assert.Contains("[JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]" + Environment.NewLine + "    [JsonPropertyName(\"display_name\")]", source);
        Assert.Contains("public string? DisplayName { get; init; }", source);
        Assert.Contains("[JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]" + Environment.NewLine + "    [JsonPropertyName(\"note\")]", source);
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

        Assert.Contains("public async Task<GetPartnerResponseModel?> ", source);
        Assert.Contains("return await response.Content.ReadFromJsonAsync<GetPartnerResponseModel?>", source);
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
        Assert.DoesNotContain("[JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]", source);
        Assert.Contains("public required string? DisplayName { get; init; }", source);
    }

    [Fact]
    public void RequiredNullableReferencedProperty_GeneratesRequiredNullableProperty()
    {
        const string openApi = """
            openapi: 3.0.1
            info:
              title: Required Nullable Reference API
              version: v1
            paths: {}
            components:
              schemas:
                partnerResponse:
                  type: object
                  required:
                    - profile
                  properties:
                    profile:
                      $ref: '#/components/schemas/profile'
                profile:
                  nullable: true
                  type: object
                  properties:
                    display_name:
                      type: string
            """;

        var source = GenerateSource(openApi);

        Assert.Contains("public required Profile? Profile { get; init; }", source);
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
