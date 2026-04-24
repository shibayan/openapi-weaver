using Microsoft.CodeAnalysis;

using Xunit;

namespace OpenApiWeaver.Tests;

public sealed partial class ClientGeneratorTests
{
    [Fact]
    public void ReservedHeaderParameters_AreIgnored()
    {
        const string openApi = """
            openapi: 3.1.0
            info:
              title: Reserved Header API
              version: v1
            paths:
              /items:
                get:
                  operationId: list_items
                  parameters:
                    - name: Accept
                      in: header
                      schema:
                        type: string
                    - name: Content-Type
                      in: header
                      schema:
                        type: string
                    - name: Authorization
                      in: header
                      schema:
                        type: string
                    - name: X-Trace-Id
                      in: header
                      required: true
                      schema:
                        type: string
                  responses:
                    '204':
                      description: ok
            """;

        var source = GenerateSource(openApi);

        Assert.Contains("ListItemsAsync(string xTraceId", source);
        Assert.DoesNotContain("string accept", source);
        Assert.DoesNotContain("string contentType", source);
        Assert.DoesNotContain("string authorization", source);
        Assert.DoesNotContain("\"Accept\"", source);
        Assert.DoesNotContain("\"Authorization\"", source);
    }

    [Fact]
    public void ReservedHeaderParameter_DeclaredOnPathItem_IsAlsoIgnored()
    {
        const string openApi = """
            openapi: 3.1.0
            info:
              title: Path-Level Reserved Header API
              version: v1
            paths:
              /items:
                parameters:
                  - name: Authorization
                    in: header
                    schema:
                      type: string
                get:
                  operationId: list_items
                  responses:
                    '204':
                      description: ok
            """;

        var source = GenerateSource(openApi);

        Assert.Contains("ListItemsAsync(CancellationToken cancellationToken", source);
        Assert.DoesNotContain("\"Authorization\"", source);
    }

    [Fact]
    public void DefaultResponse_IsUsedAsSuccess_WhenNo2xxIsDeclared()
    {
        const string openApi = """
            openapi: 3.1.0
            info:
              title: Default Success API
              version: v1
            paths:
              /partners:
                get:
                  operationId: get_partner
                  responses:
                    default:
                      description: unexpected
                      content:
                        application/json:
                          schema:
                            $ref: '#/components/schemas/partner'
            components:
              schemas:
                partner:
                  type: object
                  properties:
                    name:
                      type: string
            """;

        var source = GenerateSource(openApi);

        Assert.Contains("public async Task<Partner> GetPartnerAsync(CancellationToken cancellationToken = default)", source);
        Assert.DoesNotContain("OpenApiException<Partner>", source);
    }

    [Fact]
    public void DefaultResponse_IsTreatedAsError_WhenSuccessStatusExists()
    {
        const string openApi = """
            openapi: 3.1.0
            info:
              title: Default Error API
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
                            $ref: '#/components/schemas/partner'
                    default:
                      description: unexpected
                      content:
                        application/json:
                          schema:
                            $ref: '#/components/schemas/problem'
            components:
              schemas:
                partner:
                  type: object
                  properties:
                    name:
                      type: string
                problem:
                  type: object
                  properties:
                    message:
                      type: string
            """;

        var source = GenerateSource(openApi);

        Assert.Contains("public async Task<Partner> CreatePartnerAsync(CancellationToken cancellationToken = default)", source);
        Assert.Contains("throw new OpenApiException<Problem>(", source);
    }

    [Fact]
    public void UnknownRequestBodyContentType_ReportsUnsupportedDiagnostic()
    {
        const string openApi = """
            openapi: 3.1.0
            info:
              title: Unknown Content-Type API
              version: v1
            paths:
              /items:
                post:
                  operationId: create_item
                  requestBody:
                    required: true
                    content:
                      application/xml:
                        schema:
                          type: string
                  responses:
                    '204':
                      description: ok
            """;

        var result = RunGenerator(openApi);

        var diagnostic = Assert.Single(result.Diagnostics, static item => item.Id == "OAW004");
        Assert.Equal(DiagnosticSeverity.Error, diagnostic.Severity);
        Assert.Contains("application/xml", diagnostic.GetMessage());
    }

    [Fact]
    public void JsonFamilyRequestBodyContentType_IsAcceptedAsJson()
    {
        const string openApi = """
            openapi: 3.1.0
            info:
              title: Json Family API
              version: v1
            paths:
              /partners:
                post:
                  operationId: create_partner
                  requestBody:
                    required: true
                    content:
                      application/vnd.api+json:
                        schema:
                          $ref: '#/components/schemas/partner'
                  responses:
                    '204':
                      description: ok
            components:
              schemas:
                partner:
                  type: object
                  properties:
                    name:
                      type: string
            """;

        var source = GenerateSource(openApi);

        Assert.Contains("CreatePartnerAsync(Partner body,", source);
        Assert.Contains("JsonContent.Create(body", source);
        Assert.Contains("MediaTypeHeaderValue.Parse(\"application/vnd.api+json\")", source);
    }

    [Fact]
    public void TypelessStringEnum_GeneratesStringEnumType()
    {
        const string openApi = """
            openapi: 3.1.0
            info:
              title: Typeless Enum API
              version: v1
            paths: {}
            components:
              schemas:
                orderStatus:
                  enum:
                    - pending
                    - completed
            """;

        var source = GenerateSource(openApi);

        Assert.Contains("public readonly record struct OrderStatus(string Value)", source);
        Assert.Contains("public static readonly OrderStatus Pending = new(\"pending\");", source);
        Assert.Contains("public static readonly OrderStatus Completed = new(\"completed\");", source);
    }

    [Fact]
    public void TypelessIntegerEnum_GeneratesIntegerEnumType()
    {
        const string openApi = """
            openapi: 3.1.0
            info:
              title: Typeless Integer Enum API
              version: v1
            paths: {}
            components:
              schemas:
                orderState:
                  enum:
                    - 0
                    - 1
                    - 2
            """;

        var source = GenerateSource(openApi);

        Assert.Contains("public enum OrderState", source);
        Assert.Contains("Value0 = 0,", source);
        Assert.Contains("Value1 = 1,", source);
    }

    [Fact]
    public void TypelessNumberEnum_GeneratesNumberEnumType()
    {
        const string openApi = """
            openapi: 3.1.0
            info:
              title: Typeless Number Enum API
              version: v1
            paths: {}
            components:
              schemas:
                ratio:
                  enum:
                    - 0.25
                    - 1
                    - 1.5
            """;

        var source = GenerateSource(openApi);

        Assert.Contains("public readonly record struct Ratio(decimal Value)", source);
        Assert.Contains("public static readonly Ratio Value0_25 = new(0.25m);", source);
        Assert.Contains("public static readonly Ratio Value1 = new(1m);", source);
        Assert.Contains("public static readonly Ratio Value1_5 = new(1.5m);", source);
        Assert.Contains("return new Ratio(reader.GetDecimal());", source);
        Assert.DoesNotContain("public enum Ratio", source);
    }

    [Fact]
    public void NumberEnumWithDoubleFormat_GeneratesDoubleEnumType()
    {
        const string openApi = """
            openapi: 3.1.0
            info:
              title: Number Enum API
              version: v1
            paths: {}
            components:
              schemas:
                ratio:
                  type: number
                  format: double
                  enum:
                    - 1
                    - 2
            """;

        var source = GenerateSource(openApi);

        Assert.Contains("public readonly record struct Ratio(double Value)", source);
        Assert.Contains("public static readonly Ratio Value1 = new(1d);", source);
        Assert.Contains("public static readonly Ratio Value2 = new(2d);", source);
        Assert.Contains("return new Ratio(reader.GetDouble());", source);
        Assert.DoesNotContain("public enum Ratio", source);
    }

    [Fact]
    public void MixedTypeEnum_IsNotGeneratedAsEnum()
    {
        const string openApi = """
            openapi: 3.1.0
            info:
              title: Mixed Enum API
              version: v1
            paths: {}
            components:
              schemas:
                mixedValue:
                  enum:
                    - foo
                    - 1
            """;

        var source = GenerateSource(openApi);

        Assert.DoesNotContain("public readonly record struct MixedValue(", source);
        Assert.DoesNotContain("public enum MixedValue", source);
    }

    [Fact]
    public void ConstStringSchema_GeneratesSingleValueEnumType()
    {
        const string openApi = """
            openapi: 3.1.0
            info:
              title: Const API
              version: v1
            paths: {}
            components:
              schemas:
                kind:
                  type: string
                  const: dog
            """;

        var source = GenerateSource(openApi);

        Assert.Contains("public readonly record struct Kind(string Value)", source);
        Assert.Contains("public static readonly Kind Dog = new(\"dog\");", source);
    }

    [Fact]
    public void ConstStringSchema_WithoutType_IsInferredAsStringEnum()
    {
        const string openApi = """
            openapi: 3.1.0
            info:
              title: Typeless Const API
              version: v1
            paths: {}
            components:
              schemas:
                kind:
                  const: cat
            """;

        var source = GenerateSource(openApi);

        Assert.Contains("public readonly record struct Kind(string Value)", source);
        Assert.Contains("public static readonly Kind Cat = new(\"cat\");", source);
    }
}
