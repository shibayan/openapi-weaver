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
}
