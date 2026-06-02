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

        // DateOnly/TimeOnly only exist on net6.0+, so the date/time arms must be guarded to keep the
        // helper compilable on older target frameworks that never produce those values.
        var source = GenerateSource(openApi);
        Assert.Contains("#if NET6_0_OR_GREATER", source);

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
}
