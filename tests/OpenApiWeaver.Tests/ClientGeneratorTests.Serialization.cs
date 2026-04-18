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
}
