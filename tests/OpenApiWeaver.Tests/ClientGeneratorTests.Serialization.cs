using System.Reflection;
using System.Text.Json;

using Xunit;

namespace OpenApiWeaver.Tests;

public sealed partial class ClientGeneratorTests
{
    [Fact]
    public void DiscriminatorSchema_RoundTripsPolymorphicModels()
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
                  oneOf:
                    - $ref: '#/components/schemas/dog'
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
            """;

        using var assembly = LoadGeneratedAssembly(openApi);

        var baseType = assembly.Assembly.GetType("GeneratorTests.Animal");
        var derivedType = assembly.Assembly.GetType("GeneratorTests.Dog");
        Assert.NotNull(baseType);
        Assert.NotNull(derivedType);

        var value = JsonSerializer.Deserialize("""{"kind":"dog","name":"Pochi","barks":true}""", baseType!);
        Assert.NotNull(value);
        Assert.Equal(derivedType, value.GetType());

        var json = JsonSerializer.Serialize(value, baseType!);
        Assert.Contains("\"kind\":\"dog\"", json);
        Assert.Contains("\"name\":\"Pochi\"", json);
        Assert.Contains("\"barks\":true", json);
        Assert.Equal(1, json.Split("\"kind\"").Length - 1);
    }

    [Fact]
    public void ReadOnlyAndWriteOnlyProperties_UseDirectionalJsonSerialization()
    {
        const string openApi = """
            openapi: 3.0.1
            info:
              title: Directional Property API
              version: v1
            paths:
              /accounts:
                post:
                  operationId: create_account
                  requestBody:
                    required: true
                    content:
                      application/json:
                        schema:
                          $ref: '#/components/schemas/account'
                  responses:
                    '200':
                      description: ok
                      content:
                        application/json:
                          schema:
                            $ref: '#/components/schemas/account'
            components:
              schemas:
                account:
                  type: object
                  required:
                    - id
                    - name
                    - password
                  properties:
                    id:
                      type: integer
                      readOnly: true
                    name:
                      type: string
                    password:
                      type: string
                      writeOnly: true
            """;

        var source = GenerateSource(openApi);

        Assert.Contains("[JsonInclude]", source);
        Assert.Contains("public int Id { get; private init; }", source);
        Assert.DoesNotContain("public required int Id", source);
        Assert.Contains("public required string Password { get; init; }", source);
        Assert.Contains("options: TestClientJsonSerializerOptions.RequestSerializerOptions", source);
        Assert.Contains("ReadFromJsonAsync<Account>(TestClientJsonSerializerOptions.ResponseSerializerOptions, cancellationToken)", source);

        using var assembly = LoadGeneratedAssembly(openApi);
        var accountType = assembly.Assembly.GetType("GeneratorTests.Account");
        var optionsType = assembly.Assembly.GetType("GeneratorTests.TestClientJsonSerializerOptions");
        Assert.NotNull(accountType);
        Assert.NotNull(optionsType);

        var requestOptions = (JsonSerializerOptions)optionsType!
            .GetField("RequestSerializerOptions", BindingFlags.Static | BindingFlags.NonPublic)!
            .GetValue(null)!;
        var responseOptions = (JsonSerializerOptions)optionsType
            .GetField("ResponseSerializerOptions", BindingFlags.Static | BindingFlags.NonPublic)!
            .GetValue(null)!;

        var requestModel = JsonSerializer.Deserialize("""{"id":123,"name":"Jane","password":"secret"}""", accountType!, requestOptions);
        var requestJson = JsonSerializer.Serialize(requestModel, accountType!, requestOptions);
        Assert.DoesNotContain("\"id\"", requestJson);
        Assert.Contains("\"name\":\"Jane\"", requestJson);
        Assert.Contains("\"password\":\"secret\"", requestJson);

        var responseModel = JsonSerializer.Deserialize("""{"id":123,"name":"Jane","password":"secret"}""", accountType!, responseOptions);
        Assert.Equal(123, accountType!.GetProperty("Id")!.GetValue(responseModel));
        Assert.Equal("Jane", accountType.GetProperty("Name")!.GetValue(responseModel));
        Assert.Null(accountType.GetProperty("Password")!.GetValue(responseModel));
        Assert.Throws<JsonException>(() => JsonSerializer.Deserialize("""{"name":"Jane","password":"secret"}""", accountType, responseOptions));
    }

    [Fact]
    public void ReadOnlyPropertiesOnDictionarySchemas_UseDirectionalJsonSerialization()
    {
        const string openApi = """
            openapi: 3.0.1
            info:
              title: Directional Dictionary API
              version: v1
            paths: {}
            components:
              schemas:
                labelMap:
                  type: object
                  required:
                    - id
                  properties:
                    id:
                      type: integer
                      readOnly: true
                  additionalProperties:
                    type: string
            """;

        var source = GenerateSource(openApi);

        Assert.Contains("public int Id { get; init; }", source);
        Assert.DoesNotContain("public int Id { get; private init; }", source);

        using var assembly = LoadGeneratedAssembly(openApi);
        var mapType = assembly.Assembly.GetType("GeneratorTests.LabelMap");
        var optionsType = assembly.Assembly.GetType("GeneratorTests.TestClientJsonSerializerOptions");
        Assert.NotNull(mapType);
        Assert.NotNull(optionsType);

        var requestOptions = (JsonSerializerOptions)optionsType!
            .GetField("RequestSerializerOptions", BindingFlags.Static | BindingFlags.NonPublic)!
            .GetValue(null)!;
        var responseOptions = (JsonSerializerOptions)optionsType
            .GetField("ResponseSerializerOptions", BindingFlags.Static | BindingFlags.NonPublic)!
            .GetValue(null)!;

        var requestModel = JsonSerializer.Deserialize("""{"region":"jp"}""", mapType!, requestOptions);
        var requestJson = JsonSerializer.Serialize(requestModel, mapType!, requestOptions);
        Assert.DoesNotContain("\"id\"", requestJson);
        Assert.Contains("\"region\":\"jp\"", requestJson);
        Assert.Throws<JsonException>(() => JsonSerializer.Deserialize("""{"region":"jp"}""", mapType, responseOptions));
    }

    [Fact]
    public void FormatParameter_UsesWireFormatForBooleanAndDateTimeValues()
    {
        const string openApi = """
            openapi: 3.0.0
            info:
              title: Format API
              version: v1
            paths:
              /items:
                get:
                  operationId: listItems
                  parameters:
                    - name: active
                      in: query
                      schema:
                        type: boolean
                    - name: since
                      in: query
                      schema:
                        type: string
                        format: date-time
                    - name: day
                      in: query
                      schema:
                        type: string
                        format: date
                  responses:
                    '200':
                      description: ok
            """;

        using var assembly = LoadGeneratedAssembly(openApi);
        var helpersType = assembly.Assembly.GetType("GeneratorTests.OpenApiClientHelpers");
        Assert.NotNull(helpersType);

        // bool binds to the dedicated overload and must use the JSON/HTTP wire form, not "True"/"False".
        var boolMethod = helpersType!.GetMethod(
            "FormatParameter",
            BindingFlags.Static | BindingFlags.NonPublic,
            binder: null,
            types: [typeof(bool)],
            modifiers: null);
        Assert.NotNull(boolMethod);
        Assert.Equal("true", boolMethod!.Invoke(null, [true]));
        Assert.Equal("false", boolMethod.Invoke(null, [false]));

        // DateTimeOffset/DateOnly bind to the generic FormatParameter<T>(T) overload at the call site.
        var genericMethod = helpersType.GetMethods(BindingFlags.Static | BindingFlags.NonPublic)
            .Single(static method => method.Name == "FormatParameter"
                && method.IsGenericMethodDefinition
                && method.GetParameters() is { Length: 1 } parameters
                && parameters[0].ParameterType.IsGenericMethodParameter);

        var dateTimeOffset = new DateTimeOffset(2026, 6, 1, 13, 5, 9, TimeSpan.Zero);
        var dateTimeResult = genericMethod.MakeGenericMethod(typeof(DateTimeOffset)).Invoke(null, [dateTimeOffset]);
        Assert.Equal("2026-06-01T13:05:09.0000000+00:00", dateTimeResult);

        var dateOnly = new DateOnly(2026, 6, 1);
        var dateResult = genericMethod.MakeGenericMethod(typeof(DateOnly)).Invoke(null, [dateOnly]);
        Assert.Equal("2026-06-01", dateResult);
    }

    [Fact]
    public void DiscriminatorSchema_ToleratesServerOrderingUnknownValuesAndDirectDerivedSerialization()
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
                  oneOf:
                    - $ref: '#/components/schemas/Cat'
                  discriminator:
                    propertyName: petType
                    mapping:
                      cat: '#/components/schemas/Cat'
                Cat:
                  type: object
                  required: [petType]
                  properties:
                    petType:
                      type: string
                    name:
                      type: string
            """;

        using var assembly = LoadGeneratedAssembly(openApi);

        var petType = assembly.Assembly.GetType("GeneratorTests.Pet")!;
        var catType = assembly.Assembly.GetType("GeneratorTests.Cat")!;
        var helpersType = assembly.Assembly.GetType("GeneratorTests.OpenApiClientHelpers")!;
        var options = Assert.IsType<JsonSerializerOptions>(
            helpersType.GetField("SerializerOptions", BindingFlags.Static | BindingFlags.NonPublic)!.GetValue(null));

        // The discriminator does not have to be the first property.
        var outOfOrder = JsonSerializer.Deserialize("""{"name":"Tama","petType":"cat"}""", petType, options);
        Assert.Equal(catType, outOfOrder!.GetType());
        Assert.Equal("Tama", catType.GetProperty("Name")!.GetValue(outOfOrder));

        // A discriminator value added on the server later falls back to the base type instead of throwing.
        var unknown = JsonSerializer.Deserialize("""{"petType":"dog","name":"Pochi"}""", petType, options);
        Assert.Equal(petType, unknown!.GetType());

        // Serializing the derived type directly (e.g. as a request body) still writes its discriminator exactly once.
        var cat = JsonSerializer.Deserialize("""{"petType":"cat","name":"Tama"}""", catType, options);
        Assert.Equal(catType, cat!.GetType());
        var asCat = JsonSerializer.Serialize(cat, catType, options);
        var asPet = JsonSerializer.Serialize(cat, petType, options);
        Assert.Equal(1, asCat.Split("\"petType\":\"cat\"").Length - 1);
        Assert.Equal(1, asPet.Split("\"petType\":\"cat\"").Length - 1);
        Assert.Contains("\"name\":\"Tama\"", asCat);
    }

    [Fact]
    public void DiscriminatorWithAnyOf_GeneratesPolymorphicModels()
    {
        // ASP.NET Core's built-in OpenAPI generator describes polymorphic types with anyOf + discriminator.
        const string openApi = """
            openapi: 3.1.1
            info:
              title: Polymorphism API
              version: v1
            paths: {}
            components:
              schemas:
                Shape:
                  required: [kind]
                  type: object
                  anyOf:
                    - $ref: '#/components/schemas/ShapeCircle'
                    - $ref: '#/components/schemas/ShapeSquare'
                  discriminator:
                    propertyName: kind
                    mapping:
                      circle: '#/components/schemas/ShapeCircle'
                      square: '#/components/schemas/ShapeSquare'
                ShapeCircle:
                  type: object
                  properties:
                    kind:
                      enum: [circle]
                      type: string
                    radius:
                      type: number
                      format: double
                ShapeSquare:
                  type: object
                  properties:
                    kind:
                      enum: [square]
                      type: string
                    side:
                      type: number
                      format: double
            """;

        var result = RunGenerator(openApi);
        Assert.Empty(result.Diagnostics);

        using var assembly = LoadGeneratedAssembly(openApi);
        var shapeType = assembly.Assembly.GetType("GeneratorTests.Shape")!;
        var squareType = assembly.Assembly.GetType("GeneratorTests.ShapeSquare")!;
        Assert.Equal(shapeType, squareType.BaseType);
        var options = GetGeneratedSerializerOptions(assembly.Assembly);

        var square = JsonSerializer.Deserialize("""{"kind":"square","side":2.5}""", shapeType, options);
        Assert.Equal(squareType, square!.GetType());
        Assert.Equal(2.5, squareType.GetProperty("Side")!.GetValue(square));
        Assert.Equal(1, JsonSerializer.Serialize(square, shapeType, options).Split("\"kind\":\"square\"").Length - 1);
    }

    [Fact]
    public void DiscriminatorWithAllOfInheritance_GeneratesPolymorphicModels()
    {
        // OpenAPI 3.0 inheritance: the base declares the discriminator and subtypes extend it through allOf.
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
                  required: [petType, name]
                  properties:
                    petType:
                      type: string
                    name:
                      type: string
                  discriminator:
                    propertyName: petType
                    mapping:
                      cat: '#/components/schemas/Cat'
                Cat:
                  allOf:
                    - $ref: '#/components/schemas/Pet'
                    - type: object
                      properties:
                        huntingSkill:
                          type: string
                Dog:
                  allOf:
                    - $ref: '#/components/schemas/Pet'
                    - type: object
                      properties:
                        packSize:
                          type: integer
            """;

        var source = GenerateSource(openApi);
        Assert.Contains("[JsonDerivedType(typeof(Cat), typeDiscriminator: \"cat\")]", source);
        Assert.Contains("[JsonDerivedType(typeof(Dog), typeDiscriminator: \"Dog\")]", source);
        Assert.Contains("public class Cat : Pet", source);
        Assert.Equal(1, source.Split("public required string Name { get; init; }").Length - 1);

        using var assembly = LoadGeneratedAssembly(openApi);
        var petType = assembly.Assembly.GetType("GeneratorTests.Pet")!;
        var dogType = assembly.Assembly.GetType("GeneratorTests.Dog")!;
        var options = GetGeneratedSerializerOptions(assembly.Assembly);

        var dog = JsonSerializer.Deserialize("""{"name":"Pochi","packSize":3,"petType":"Dog"}""", petType, options);
        Assert.Equal(dogType, dog!.GetType());
        Assert.Equal("Pochi", petType.GetProperty("Name")!.GetValue(dog));
        Assert.Equal(3, dogType.GetProperty("PackSize")!.GetValue(dog));

        var json = JsonSerializer.Serialize(dog, dogType, options);
        Assert.Equal(1, json.Split("\"petType\":\"Dog\"").Length - 1);
        Assert.Contains("\"name\":\"Pochi\"", json);
    }

    [Fact]
    public void DiscriminatorWithAllOfInheritance_AppliesInheritedReadOnlyAndWriteOnlyRules()
    {
        const string openApi = """
            openapi: 3.0.1
            info:
              title: Polymorphism API
              version: v1
            paths:
              /cats:
                post:
                  operationId: createCat
                  requestBody:
                    required: true
                    content:
                      application/json:
                        schema:
                          $ref: '#/components/schemas/Cat'
                  responses:
                    '200':
                      description: ok
                      content:
                        application/json:
                          schema:
                            $ref: '#/components/schemas/Cat'
            components:
              schemas:
                Pet:
                  type: object
                  required: [petType, id, password]
                  properties:
                    petType:
                      type: string
                    id:
                      type: integer
                      readOnly: true
                    password:
                      type: string
                      writeOnly: true
                  discriminator:
                    propertyName: petType
                Cat:
                  allOf:
                    - $ref: '#/components/schemas/Pet'
                    - type: object
                      properties:
                        name:
                          type: string
            """;

        using var assembly = LoadGeneratedAssembly(openApi);
        var catType = assembly.Assembly.GetType("GeneratorTests.Cat")!;
        var optionsType = assembly.Assembly.GetType("GeneratorTests.TestClientJsonSerializerOptions")!;
        var requestOptions = Assert.IsType<JsonSerializerOptions>(optionsType.GetField("RequestSerializerOptions", BindingFlags.Static | BindingFlags.NonPublic)!.GetValue(null));
        var responseOptions = Assert.IsType<JsonSerializerOptions>(optionsType.GetField("ResponseSerializerOptions", BindingFlags.Static | BindingFlags.NonPublic)!.GetValue(null));

        // A response omits the base's writeOnly property even though it is required.
        var cat = JsonSerializer.Deserialize("""{"petType":"Cat","id":7,"name":"Tama"}""", catType, responseOptions);
        Assert.NotNull(cat);
        Assert.Equal(7, catType.GetProperty("Id")!.GetValue(cat));

        // A request does not send the base's readOnly property.
        catType.GetProperty("Password")!.SetValue(cat, "secret");
        var json = JsonSerializer.Serialize(cat, catType, requestOptions);
        Assert.DoesNotContain("\"id\"", json);
        Assert.Contains("\"password\":\"secret\"", json);
        Assert.Contains("\"petType\":\"Cat\"", json);
    }

    [Fact]
    public void DiscriminatorWithMultiLevelAllOfInheritance_IncludesTransitiveSubtypes()
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
                    mapping:
                      cat: '#/components/schemas/Cat'
                      siamese: '#/components/schemas/Siamese'
                Cat:
                  allOf:
                    - $ref: '#/components/schemas/Pet'
                    - type: object
                      properties:
                        name:
                          type: string
                Siamese:
                  allOf:
                    - $ref: '#/components/schemas/Cat'
                    - type: object
                      properties:
                        pointColor:
                          type: string
            """;

        var source = GenerateSource(openApi);
        Assert.Contains("public class Siamese : Cat", source);
        // Registered on Pet, on the intermediate Cat, and on Siamese itself.
        Assert.Equal(3, source.Split("[JsonDerivedType(typeof(Siamese), typeDiscriminator: \"siamese\")]").Length - 1);
        Assert.Equal(1, source.Split("public string? Name { get; init; }").Length - 1);

        using var assembly = LoadGeneratedAssembly(openApi);
        var petType = assembly.Assembly.GetType("GeneratorTests.Pet")!;
        var catType = assembly.Assembly.GetType("GeneratorTests.Cat")!;
        var siameseType = assembly.Assembly.GetType("GeneratorTests.Siamese")!;
        var options = GetGeneratedSerializerOptions(assembly.Assembly);

        const string siameseJson = """{"name":"Tama","pointColor":"seal","petType":"siamese"}""";
        var viaPet = JsonSerializer.Deserialize(siameseJson, petType, options);
        var viaCat = JsonSerializer.Deserialize(siameseJson, catType, options);
        Assert.Equal(siameseType, viaPet!.GetType());
        Assert.Equal(siameseType, viaCat!.GetType());
        Assert.Equal("Tama", catType.GetProperty("Name")!.GetValue(viaPet));

        foreach (var declaredType in new[] { petType, catType, siameseType })
        {
            var json = JsonSerializer.Serialize(viaPet, declaredType, options);
            Assert.Equal(1, json.Split("\"petType\":\"siamese\"").Length - 1);
            Assert.Contains("\"pointColor\":\"seal\"", json);
        }
    }

    [Fact]
    public void DiscriminatorWithAllOfInheritance_MergesRedeclaredAndRequiredInheritedProperties()
    {
        // Polymorphic counterpart of AllOfSchema_RequiredOnComposedSchema_AppliesToReferencedProperties.
        const string openApi = """
            openapi: 3.0.1
            info:
              title: Polymorphism API
              version: v1
            paths:
              /cats:
                get:
                  operationId: getCat
                  responses:
                    '200':
                      description: ok
                      content:
                        application/json:
                          schema:
                            $ref: '#/components/schemas/Cat'
            components:
              schemas:
                Pet:
                  type: object
                  required: [petType]
                  properties:
                    petType:
                      type: string
                    name:
                      type: string
                    tag:
                      type: string
                    pet_name:
                      type: string
                  discriminator:
                    propertyName: petType
                Cat:
                  allOf:
                    - $ref: '#/components/schemas/Pet'
                    - type: object
                      required: [name]
                      properties:
                        tag:
                          type: string
                        petName:
                          type: string
            """;

        var source = GenerateSource(openApi);

        // `tag` is redeclared with the same type and reuses Pet.Tag; `petName` is a distinct JSON property whose
        // CLR name would hide Pet.PetName, so it is renamed.
        Assert.Equal(1, source.Split("public string? Tag { get; init; }").Length - 1);
        Assert.Contains("public string? PetName { get; init; }", source);
        Assert.Contains("[JsonPropertyName(\"petName\")]", source);
        Assert.Contains("public string? PetName2 { get; init; }", source);

        using var assembly = LoadGeneratedAssembly(openApi);
        var petType = assembly.Assembly.GetType("GeneratorTests.Pet")!;
        var catType = assembly.Assembly.GetType("GeneratorTests.Cat")!;
        var optionsType = assembly.Assembly.GetType("GeneratorTests.TestClientJsonSerializerOptions")!;
        var responseOptions = Assert.IsType<JsonSerializerOptions>(optionsType.GetField("ResponseSerializerOptions", BindingFlags.Static | BindingFlags.NonPublic)!.GetValue(null));

        // Cat makes Pet's optional `name` required, so a Cat response without it is rejected, directly or through Pet.
        Assert.Throws<JsonException>(() => JsonSerializer.Deserialize("""{"petType":"Cat","tag":"x"}""", catType, responseOptions));
        Assert.Throws<JsonException>(() => JsonSerializer.Deserialize("""{"petType":"Cat","tag":"x"}""", petType, responseOptions));

        var cat = JsonSerializer.Deserialize("""{"petType":"Cat","name":"Tama"}""", petType, responseOptions);
        Assert.Equal(catType, cat!.GetType());
        Assert.Equal("Tama", petType.GetProperty("Name")!.GetValue(cat));

        // Pet itself keeps `name` optional.
        var pet = JsonSerializer.Deserialize("""{"tag":"x"}""", petType, responseOptions);
        Assert.Equal(petType, pet!.GetType());
    }

    [Fact]
    public void DiscriminatorWithAllOfInheritance_AppliesDirectionAnnotationsAddedByRedeclaration()
    {
        const string openApi = """
            openapi: 3.0.1
            info:
              title: Polymorphism API
              version: v1
            paths:
              /cats:
                post:
                  operationId: createCat
                  requestBody:
                    required: true
                    content:
                      application/json:
                        schema:
                          $ref: '#/components/schemas/Cat'
                  responses:
                    '200':
                      description: ok
                      content:
                        application/json:
                          schema:
                            $ref: '#/components/schemas/Cat'
            components:
              schemas:
                Pet:
                  type: object
                  required: [petType, password, nickname]
                  properties:
                    petType:
                      type: string
                    password:
                      type: string
                    nickname:
                      type: string
                  discriminator:
                    propertyName: petType
                Cat:
                  allOf:
                    - $ref: '#/components/schemas/Pet'
                    - type: object
                      properties:
                        password:
                          type: string
                          writeOnly: true
                        nickname:
                          type: string
                          readOnly: true
            """;

        using var assembly = LoadGeneratedAssembly(openApi);
        var petType = assembly.Assembly.GetType("GeneratorTests.Pet")!;
        var catType = assembly.Assembly.GetType("GeneratorTests.Cat")!;
        var optionsType = assembly.Assembly.GetType("GeneratorTests.TestClientJsonSerializerOptions")!;
        var requestOptions = Assert.IsType<JsonSerializerOptions>(optionsType.GetField("RequestSerializerOptions", BindingFlags.Static | BindingFlags.NonPublic)!.GetValue(null));
        var responseOptions = Assert.IsType<JsonSerializerOptions>(optionsType.GetField("ResponseSerializerOptions", BindingFlags.Static | BindingFlags.NonPublic)!.GetValue(null));

        // Cat makes `password` writeOnly: a Cat response omits it, even though Pet requires it.
        var cat = JsonSerializer.Deserialize("""{"petType":"Cat","nickname":"Tama"}""", catType, responseOptions);
        Assert.NotNull(cat);
        Assert.Null(petType.GetProperty("Password")!.GetValue(cat));

        // Cat makes `nickname` readOnly: a Cat request does not send it.
        petType.GetProperty("Password")!.SetValue(cat, "secret");
        var json = JsonSerializer.Serialize(cat, catType, requestOptions);
        Assert.DoesNotContain("\"nickname\"", json);
        Assert.Contains("\"password\":\"secret\"", json);

        // Pet itself keeps both properties bidirectional.
        Assert.Throws<JsonException>(() => JsonSerializer.Deserialize("""{"nickname":"Tama"}""", petType, responseOptions));
    }

    [Theory]
    // Cat requires `password` and Siamese makes it writeOnly; components are listed subtype-first.
    [InlineData(
        "required: [password]",
        "properties:\n  password:\n    type: string\n    writeOnly: true",
        false)]
    // Cat makes `password` writeOnly and Siamese requires it, which must not make it required in responses.
    [InlineData(
        "properties:\n  password:\n    type: string\n    writeOnly: true",
        "required: [password]",
        true)]
    public void MultiLevelInheritance_AppliesDirectionAndRequirementRulesAncestorFirst(string catMemberYaml, string siameseMemberYaml, bool catAllowsMissingPassword)
    {
        static string Indent(string yaml) => string.Join("\n", yaml.Split('\n').Select(static line => "          " + line));

        var openApi = $$"""
            openapi: 3.0.1
            info:
              title: Polymorphism API
              version: v1
            paths: {}
            components:
              schemas:
                Siamese:
                  allOf:
                    - $ref: '#/components/schemas/Cat'
                    - type: object
            {{Indent(siameseMemberYaml)}}
                Cat:
                  allOf:
                    - $ref: '#/components/schemas/Pet'
                    - type: object
            {{Indent(catMemberYaml)}}
                Pet:
                  type: object
                  required: [petType]
                  properties:
                    petType:
                      type: string
                    password:
                      type: string
                  discriminator:
                    propertyName: petType
            """;

        using var assembly = LoadGeneratedAssembly(openApi);
        var petType = assembly.Assembly.GetType("GeneratorTests.Pet")!;
        var catType = assembly.Assembly.GetType("GeneratorTests.Cat")!;
        var siameseType = assembly.Assembly.GetType("GeneratorTests.Siamese")!;
        var optionsType = assembly.Assembly.GetType("GeneratorTests.TestClientJsonSerializerOptions")!;
        var responseOptions = Assert.IsType<JsonSerializerOptions>(optionsType.GetField("ResponseSerializerOptions", BindingFlags.Static | BindingFlags.NonPublic)!.GetValue(null));

        // In both shapes the password is writeOnly for Siamese, so a Siamese response without it is valid.
        var siamese = JsonSerializer.Deserialize("""{"petType":"Siamese"}""", siameseType, responseOptions);
        Assert.Equal(siameseType, siamese!.GetType());
        Assert.Equal(siameseType, JsonSerializer.Deserialize("""{"petType":"Siamese"}""", petType, responseOptions)!.GetType());

        if (catAllowsMissingPassword)
        {
            Assert.NotNull(JsonSerializer.Deserialize("""{"petType":"Cat"}""", catType, responseOptions));
        }
        else
        {
            Assert.Throws<JsonException>(() => JsonSerializer.Deserialize("""{"petType":"Cat"}""", catType, responseOptions));
        }
    }

    [Fact]
    public void AllOfSubtype_WithRedundantAncestorReference_ExtendsMostSpecificParent()
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
                      properties:
                        name:
                          type: string
                Siamese:
                  allOf:
                    - $ref: '#/components/schemas/Pet'
                    - $ref: '#/components/schemas/Cat'
                    - type: object
                      properties:
                        pointColor:
                          type: string
            """;

        // Siamese references Pet redundantly; it must still derive from Cat and be registered on Cat.
        var source = GenerateSource(openApi);
        Assert.Contains("public class Siamese : Cat", source);

        using var assembly = LoadGeneratedAssembly(openApi);
        var catType = assembly.Assembly.GetType("GeneratorTests.Cat")!;
        var siameseType = assembly.Assembly.GetType("GeneratorTests.Siamese")!;
        var options = GetGeneratedSerializerOptions(assembly.Assembly);

        var viaCat = JsonSerializer.Deserialize("""{"petType":"Siamese","name":"Tama","pointColor":"seal"}""", catType, options);
        Assert.Equal(siameseType, viaCat!.GetType());
        Assert.Equal("seal", siameseType.GetProperty("PointColor")!.GetValue(viaCat));
    }

    private static JsonSerializerOptions GetGeneratedSerializerOptions(Assembly assembly)
        => Assert.IsType<JsonSerializerOptions>(
            assembly.GetType("GeneratorTests.OpenApiClientHelpers")!
                .GetField("SerializerOptions", BindingFlags.Static | BindingFlags.NonPublic)!
                .GetValue(null));
}
