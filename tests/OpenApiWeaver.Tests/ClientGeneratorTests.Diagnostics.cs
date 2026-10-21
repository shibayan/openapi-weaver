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
    public void PipeDelimitedQueryParameter_ReportsUnsupportedDiagnostic_AndSkipsOperation()
    {
        const string openApi = """
            openapi: 3.0.1
            info:
              title: Parameter Style API
              version: v1
            paths:
              /reports:
                get:
                  operationId: list_reports
                  parameters:
                    - name: ids
                      in: query
                      required: true
                      style: pipeDelimited
                      schema:
                        type: array
                        items:
                          type: integer
                  responses:
                    '200':
                      description: ok
                      content:
                        application/json:
                          schema:
                            type: object
            """;

        var result = RunGenerator(openApi);

        var diagnostic = AssertOnlyOperationSkippedDiagnostic(result, ParameterUnsupportedDiagnosticId, "ListReports");
        Assert.Contains("PipeDelimited", diagnostic.GetMessage());
    }

    [Fact]
    public void NonExplodedArrayQueryParameter_ReportsUnsupportedDiagnostic_AndSkipsOperation()
    {
        const string openApi = """
            openapi: 3.0.1
            info:
              title: Parameter Style API
              version: v1
            paths:
              /reports:
                get:
                  operationId: list_reports
                  parameters:
                    - name: ids
                      in: query
                      required: true
                      explode: false
                      schema:
                        type: array
                        items:
                          type: integer
                  responses:
                    '200':
                      description: ok
                      content:
                        application/json:
                          schema:
                            type: object
            """;

        var result = RunGenerator(openApi);

        var diagnostic = AssertOnlyOperationSkippedDiagnostic(result, ParameterUnsupportedDiagnosticId, "ListReports");
        Assert.Contains("explode: false", diagnostic.GetMessage());
    }

    [Fact]
    public void NonExplodedScalarQueryParameter_GeneratesClient()
    {
        const string openApi = """
            openapi: 3.0.1
            info:
              title: Parameter Style API
              version: v1
            paths:
              /reports:
                get:
                  operationId: list_reports
                  parameters:
                    - name: page
                      in: query
                      required: true
                      explode: false
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

        Assert.Contains("OpenApiClientHelpers.AppendQueryParameter(pathBuilder, ref hasQuery, \"page\", OpenApiClientHelpers.FormatParameter(page));", source);
    }

    [Fact]
    public void InlineMultipartRequestBody_ReportsUnsupportedDiagnostic_AndSkipsOperation()
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

        var diagnostic = AssertOnlyOperationSkippedDiagnostic(result, RequestBodyUnsupportedDiagnosticId, "CreateUpload");
        Assert.Contains("multipart/form-data request bodies must reference a named component schema", diagnostic.GetMessage());
    }

    [Fact]
    public void AdditionalPropertiesFormRequestBody_ReportsUnsupportedDiagnostic_AndSkipsOperation()
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

        var diagnostic = AssertOnlyOperationSkippedDiagnostic(result, RequestBodyUnsupportedDiagnosticId, "CreateReport");
        Assert.Contains("additionalProperties or patternProperties", diagnostic.GetMessage());
    }

    [Fact]
    public void AnyOfMultipartRequestBody_ReportsUnsupportedDiagnostic_AndSkipsOperation()
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

        var diagnostic = AssertOnlyOperationSkippedDiagnostic(result, RequestBodyUnsupportedDiagnosticId, "CreateUpload");
        Assert.Contains("uses oneOf/anyOf", diagnostic.GetMessage());
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
    public void DiscriminatorListingItselfAsMember_ReportsUnsupportedDiagnostic()
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
                  required: [kind]
                  discriminator:
                    propertyName: kind
                  oneOf:
                    - $ref: '#/components/schemas/animal'
                    - $ref: '#/components/schemas/dog'
                  properties:
                    kind:
                      type: string
                dog:
                  type: object
                  properties:
                    barks:
                      type: boolean
            """;

        // Previously the subtype ancestor walk never terminated for a self-referencing member.
        var result = RunGenerator(openApi);

        var diagnostic = AssertSingleErrorDiagnostic(result, DiscriminatorUnsupportedDiagnosticId);
        Assert.Contains("lists itself as a discriminator oneOf member", diagnostic.GetMessage());
        AssertNoClientSource(result);
    }

    [Theory]
    [InlineData(
        "additionalProperties:\n  type: string",
        "uses additionalProperties or patternProperties in the 'Pet' discriminator hierarchy")]
    [InlineData(
        "properties:\n  name:\n    type: integer",
        "redeclares inherited property 'name' as 'int' instead of 'string'")]
    public void UnsupportedAllOfInheritanceShapes_ReportDiscriminatorDiagnostic(string catMemberYaml, string expectedMessage)
    {
        var indentedMember = string.Join("\n", catMemberYaml.Split('\n').Select(static line => "          " + line));
        var openApi = $$"""
            openapi: 3.0.1
            info:
              title: Polymorphism API
              version: v1
            paths: {}
            components:
              schemas:
                Pet:
                  type: object
                  required: [petType, name]
                  properties:
                    petType:
                      type: string
                    name:
                      type: string
                  discriminator:
                    propertyName: petType
                Cat:
                  allOf:
                    - $ref: '#/components/schemas/Pet'
                    - type: object
            {{indentedMember}}
                Siamese:
                  allOf:
                    - $ref: '#/components/schemas/Cat'
            """;

        var result = RunGenerator(openApi);

        var diagnostic = AssertSingleErrorDiagnostic(result, DiscriminatorUnsupportedDiagnosticId);
        Assert.Contains(expectedMessage, diagnostic.GetMessage());
        AssertNoClientSource(result);
    }

    [Fact]
    public void AllOfSubtypeExtendingUnrelatedSubtypes_ReportsUnsupportedDiagnostic()
    {
        const string openApi = """
            openapi: 3.0.1
            info:
              title: Polymorphism API
              version: v1
            paths: {}
            components:
              schemas:
                Pet:
                  type: object
                  required: [petType]
                  properties:
                    petType:
                      type: string
                  discriminator:
                    propertyName: petType
                Cat:
                  allOf:
                    - $ref: '#/components/schemas/Pet'
                Dog:
                  allOf:
                    - $ref: '#/components/schemas/Pet'
                CatDog:
                  allOf:
                    - $ref: '#/components/schemas/Cat'
                    - $ref: '#/components/schemas/Dog'
            """;

        // Multiple inheritance cannot be represented, so it is rejected instead of silently picking one parent.
        var result = RunGenerator(openApi);

        var diagnostic = AssertSingleErrorDiagnostic(result, DiscriminatorUnsupportedDiagnosticId);
        Assert.Contains("'CatDog' extends both", diagnostic.GetMessage());
        AssertNoClientSource(result);
    }

    [Fact]
    public void NestedDiscriminatorHierarchy_ReportsUnsupportedDiagnostic()
    {
        const string openApi = """
            openapi: 3.0.1
            info:
              title: Polymorphism API
              version: v1
            paths: {}
            components:
              schemas:
                Pet:
                  type: object
                  required: [petType]
                  properties:
                    petType:
                      type: string
                  discriminator:
                    propertyName: petType
                Cat:
                  allOf:
                    - $ref: '#/components/schemas/Pet'
                    - type: object
                      required: [catKind]
                      properties:
                        catKind:
                          type: string
                  discriminator:
                    propertyName: catKind
                Siamese:
                  allOf:
                    - $ref: '#/components/schemas/Cat'
            """;

        // Cat would need both Pet's and its own discriminator contract, which cannot round-trip.
        var result = RunGenerator(openApi);

        var diagnostic = AssertSingleErrorDiagnostic(result, DiscriminatorUnsupportedDiagnosticId);
        Assert.Contains("declares its own discriminator hierarchy", diagnostic.GetMessage());
        AssertNoClientSource(result);
    }

    [Fact]
    public void DiscriminatorWithoutDerivedSchemas_KeepsDiscriminatorAsOrdinaryProperty()
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
                  required: [kind]
                  discriminator:
                    propertyName: kind
                  properties:
                    kind:
                      type: string
            """;

        var result = RunGenerator(openApi);

        Assert.Empty(result.Diagnostics);
        var source = string.Join(Environment.NewLine, result.GeneratedSources);
        Assert.Contains("public sealed class Animal", source);
        Assert.Contains("[JsonPropertyName(\"kind\")]", source);
        Assert.DoesNotContain("JsonPolymorphic", source);
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
