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

    private static JsonSerializerOptions GetGeneratedSerializerOptions(Assembly assembly)
        => Assert.IsType<JsonSerializerOptions>(
            assembly.GetType("GeneratorTests.OpenApiClientHelpers")!
                .GetField("SerializerOptions", BindingFlags.Static | BindingFlags.NonPublic)!
                .GetValue(null));
}
