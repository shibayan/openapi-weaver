using Xunit;

namespace OpenApiWeaver.Tests;

public sealed partial class ClientGeneratorTests
{
    [Fact]
    public void OpenApiWeaverDocumentClientNameMetadata_OverridesGeneratedClientName()
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
            [BuildMetadataAdditionalFilesClientName] = "ContosoSdk"
        });

        Assert.Contains("public partial class ContosoSdkClient : IDisposable", source);
        Assert.DoesNotContain("public partial class TestClient : IDisposable", source);
    }

    [Fact]
    public void OpenApiWeaverDocumentNamespaceMetadata_OverridesRootNamespace()
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
            [BuildMetadataAdditionalFilesNamespace] = "Contoso.Generated"
        });

        Assert.Contains("namespace Contoso.Generated;", source);
        Assert.DoesNotContain("namespace GeneratorTests;", source);
    }

    [Fact]
    public void AdditionalFiles_AreIgnored()
    {
        const string openApi = """
            openapi: 3.0.1
            info:
              title: Petstore API
              version: 1.0.0
            paths: {}
            """;

        var result = RunGenerator(openApi, isOpenApiWeaverDocument: false);

        Assert.Empty(result.Diagnostics);
        Assert.Empty(result.GeneratedSources);
    }
}
