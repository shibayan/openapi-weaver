using Microsoft.CodeAnalysis;

using Xunit;

namespace OpenApiWeaver.Tests;

public sealed partial class ClientGeneratorTests
{
    [Fact]
    public void EmptyDocument_ReportsDiagnostic_AndDoesNotGenerateSource()
    {
        var result = RunGenerator("   ");

        var diagnostic = Assert.Single(result.Diagnostics);
        Assert.Equal("OAW001", diagnostic.Id);
        Assert.Equal(DiagnosticSeverity.Error, diagnostic.Severity);
        AssertNoClientSource(result);
    }

    [Fact]
    public void InvalidDocument_ReportsDiagnostic_AndDoesNotGenerateSource()
    {
        var result = RunGenerator("not: [valid");

        var diagnostic = Assert.Single(result.Diagnostics);
        Assert.Equal("OAW003", diagnostic.Id);
        Assert.Equal(DiagnosticSeverity.Error, diagnostic.Severity);
        AssertNoClientSource(result);
    }

    [Fact]
    public void InlineMultipartRequestBody_ReportsUnsupportedDiagnostic_AndDoesNotGenerateSource()
    {
        const string openApi = """
            openapi: 3.0.1
            info:
              title: Inline Multipart API
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
                          type: object
                          properties:
                            file:
                              type: string
                              format: binary
                  responses:
                    '201':
                      description: created
                      content:
                        application/json:
                          schema:
                            type: object
            """;

        var result = RunGenerator(openApi);

        var diagnostic = Assert.Single(result.Diagnostics);
        Assert.Equal("OAW004", diagnostic.Id);
        Assert.Equal(DiagnosticSeverity.Error, diagnostic.Severity);
        Assert.Contains("multipart/form-data request bodies must reference a named component schema", diagnostic.GetMessage());
        AssertNoClientSource(result);
    }

    [Fact]
    public void AdditionalPropertiesFormRequestBody_ReportsUnsupportedDiagnostic_AndDoesNotGenerateSource()
    {
        const string openApi = """
            openapi: 3.0.1
            info:
              title: Form Dictionary API
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
                          $ref: '#/components/schemas/reportCreateParams'
                  responses:
                    '201':
                      description: created
                      content:
                        application/json:
                          schema:
                            type: object
            components:
              schemas:
                reportCreateParams:
                  type: object
                  additionalProperties:
                    type: string
            """;

        var result = RunGenerator(openApi);

        var diagnostic = Assert.Single(result.Diagnostics);
        Assert.Equal("OAW004", diagnostic.Id);
        Assert.Equal(DiagnosticSeverity.Error, diagnostic.Severity);
        Assert.Contains("additionalProperties or patternProperties", diagnostic.GetMessage());
        AssertNoClientSource(result);
    }

    [Fact]
    public void AnyOfMultipartRequestBody_ReportsUnsupportedDiagnostic_AndDoesNotGenerateSource()
    {
        const string openApi = """
            openapi: 3.0.1
            info:
              title: Multipart Union API
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
                  anyOf:
                    - type: object
                      properties:
                        file:
                          type: string
                          format: binary
                    - type: object
                      properties:
                        description:
                          type: string
            """;

        var result = RunGenerator(openApi);

        var diagnostic = Assert.Single(result.Diagnostics);
        Assert.Equal("OAW004", diagnostic.Id);
        Assert.Equal(DiagnosticSeverity.Error, diagnostic.Severity);
        Assert.Contains("uses oneOf/anyOf", diagnostic.GetMessage());
        AssertNoClientSource(result);
    }

    private static void AssertNoClientSource(GeneratorTestResult result)
    {
        Assert.DoesNotContain(result.GeneratedSources, static source => source.Contains(": IDisposable"));
    }
}
