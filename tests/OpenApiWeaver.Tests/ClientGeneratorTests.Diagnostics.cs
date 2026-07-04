using Xunit;

namespace OpenApiWeaver.Tests;

public sealed partial class ClientGeneratorTests
{
    [Fact]
    public void EmptyDocument_ReportsDiagnostic_AndDoesNotGenerateSource()
    {
        var result = RunGenerator("   ");

        AssertOnlyErrorDiagnostic(result, DocumentEmptyDiagnosticId);
        AssertNoClientSource(result);
    }

    [Fact]
    public void InvalidDocument_ReportsDiagnostic_AndDoesNotGenerateSource()
    {
        var result = RunGenerator("not: [valid");

        AssertOnlyErrorDiagnostic(result, DocumentInvalidDiagnosticId);
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

        var diagnostic = AssertOnlyErrorDiagnostic(result, RequestBodyUnsupportedDiagnosticId);
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

        var diagnostic = AssertOnlyErrorDiagnostic(result, RequestBodyUnsupportedDiagnosticId);
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

        var diagnostic = AssertOnlyErrorDiagnostic(result, RequestBodyUnsupportedDiagnosticId);
        Assert.Contains("uses oneOf/anyOf", diagnostic.GetMessage());
        AssertNoClientSource(result);
    }

    [Fact]
    public void DiscriminatorWithInlineOneOfMember_ReportsUnsupportedDiagnostic_AndDoesNotGenerateSource()
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
                  discriminator:
                    propertyName: kind
                  oneOf:
                    - type: object
                      properties:
                        kind:
                          type: string
            """;

        var result = RunGenerator(openApi);

        var diagnostic = AssertOnlyErrorDiagnostic(result, DiscriminatorUnsupportedDiagnosticId);
        Assert.Contains("inline oneOf members", diagnostic.GetMessage());
        AssertNoClientSource(result);
    }

    [Fact]
    public void DiscriminatorWithoutOneOf_ReportsUnsupportedDiagnostic_AndDoesNotGenerateSource()
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
                  properties:
                    kind:
                      type: string
            """;

        var result = RunGenerator(openApi);

        var diagnostic = AssertSingleErrorDiagnostic(result, DiscriminatorUnsupportedDiagnosticId);
        Assert.Contains("without oneOf", diagnostic.GetMessage());
        AssertNoClientSource(result);
    }

    [Fact]
    public void DiscriminatorWithAnyOf_ReportsUnsupportedDiagnostic_AndDoesNotGenerateSource()
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
                  discriminator:
                    propertyName: kind
                  anyOf:
                    - $ref: '#/components/schemas/dog'
                dog:
                  type: object
                  properties:
                    kind:
                      type: string
            """;

        var result = RunGenerator(openApi);

        var diagnostic = AssertSingleErrorDiagnostic(result, DiscriminatorUnsupportedDiagnosticId);
        Assert.Contains("discriminator with anyOf", diagnostic.GetMessage());
        AssertNoClientSource(result);
    }

    [Fact]
    public void DiscriminatorWithDuplicateMappingValue_ReportsUnsupportedDiagnostic_AndDoesNotGenerateSource()
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
                      cat: '#/components/schemas/dog'
                  oneOf:
                    - $ref: '#/components/schemas/dog'
                    - $ref: '#/components/schemas/cat'
                dog:
                  allOf:
                    - $ref: '#/components/schemas/animal'
                    - type: object
                      properties:
                        barks:
                          type: boolean
                cat:
                  allOf:
                    - $ref: '#/components/schemas/animal'
                    - type: object
                      properties:
                        lives:
                          type: integer
            """;

        var result = RunGenerator(openApi);

        var diagnostic = AssertSingleErrorDiagnostic(result, DiscriminatorUnsupportedDiagnosticId);
        Assert.Contains("duplicate discriminator value", diagnostic.GetMessage());
        AssertNoClientSource(result);
    }

    [Fact]
    public void DiscriminatorMappingReferencesSchemaNotInOneOf_ReportsUnsupportedDiagnostic_AndDoesNotGenerateSource()
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
                      fish: '#/components/schemas/fish'
                  oneOf:
                    - $ref: '#/components/schemas/dog'
                dog:
                  allOf:
                    - $ref: '#/components/schemas/animal'
                    - type: object
                      properties:
                        barks:
                          type: boolean
                fish:
                  type: object
                  properties:
                    fins:
                      type: integer
            """;

        var result = RunGenerator(openApi);

        var diagnostic = AssertSingleErrorDiagnostic(result, DiscriminatorUnsupportedDiagnosticId);
        Assert.Contains("must reference a schema listed in oneOf", diagnostic.GetMessage());
        AssertNoClientSource(result);
    }

    [Fact]
    public void DiscriminatorSharedDerivedTypeAcrossHierarchies_ReportsUnsupportedDiagnostic_AndDoesNotGenerateSource()
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
                  oneOf:
                    - $ref: '#/components/schemas/dog'
                pet:
                  type: object
                  discriminator:
                    propertyName: category
                  oneOf:
                    - $ref: '#/components/schemas/dog'
                dog:
                  type: object
                  properties:
                    kind:
                      type: string
                    category:
                      type: string
                    barks:
                      type: boolean
            """;

        var result = RunGenerator(openApi);

        var diagnostic = AssertSingleErrorDiagnostic(result, DiscriminatorUnsupportedDiagnosticId);
        Assert.Contains("multiple discriminator hierarchies", diagnostic.GetMessage());
        AssertNoClientSource(result);
    }

    [Fact]
    public void SchemaPropertyMarkedReadOnlyAndWriteOnly_ReportsUnsupportedDiagnostic()
    {
        const string openApi = """
            openapi: 3.0.1
            info:
              title: RO/WO API
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
                            $ref: '#/components/schemas/Report'
            components:
              schemas:
                Report:
                  type: object
                  properties:
                    id:
                      type: string
                      readOnly: true
                      writeOnly: true
            """;

        var result = RunGenerator(openApi);

        var diagnostic = AssertSingleErrorDiagnostic(result, SchemaUnsupportedDiagnosticId);
        Assert.Contains("both readOnly and writeOnly", diagnostic.GetMessage());
        AssertNoClientSource(result);
    }
}
