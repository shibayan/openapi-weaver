using Xunit;

namespace OpenApiWeaver.Tests;

public sealed partial class OpenApiWeaverSourceGeneratorTests
{
    [Fact]
    public void AdditionalFileClientNameMetadata_OverridesGeneratedClientName()
    {
        const string openApi = """
            openapi: 3.0.1
            info:
              title: Petstore API
              version: 1.0.0
            paths: {}
            """;

        var source = GenerateSource(openApi, new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["build_metadata.AdditionalFiles.ClientName"] = "ContosoSdk"
        });

        Assert.Contains("public partial class ContosoSdkClient : IDisposable", source);
        Assert.DoesNotContain("public partial class TestClient : IDisposable", source);
    }

    [Fact]
    public void AdditionalFileNamespaceMetadata_OverridesRootNamespace()
    {
        const string openApi = """
            openapi: 3.0.1
            info:
              title: Petstore API
              version: 1.0.0
            paths: {}
            """;

        var source = GenerateSource(openApi, new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["build_metadata.AdditionalFiles.Namespace"] = "Contoso.Generated"
        });

        Assert.Contains("namespace Contoso.Generated;", source);
        Assert.DoesNotContain("namespace GeneratorTests;", source);
    }
}
