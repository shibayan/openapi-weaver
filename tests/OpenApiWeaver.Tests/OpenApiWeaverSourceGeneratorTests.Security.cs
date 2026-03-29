using Xunit;

namespace OpenApiWeaver.Tests;

public sealed partial class OpenApiWeaverSourceGeneratorTests
{
    [Fact]
    public void QueryApiKeySecurityScheme_GeneratesConstructorParameter_AndAppendsToPath()
    {
        const string openApi = """
            openapi: 3.0.1
            info:
              title: Query Security API
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
                            type: object
            components:
              securitySchemes:
                partner:
                  type: apiKey
                  in: query
                  name: api_key
        """;

        var source = GenerateSource(openApi);

        Assert.Contains("private readonly string? _partnerApiKey;", source);
        Assert.Contains("public partial class TestClient : IDisposable", source);
        Assert.Contains("public TestClient(string? partnerApiKey = default)", source);
        Assert.Contains("_partnerApiKey = partnerApiKey;", source);
        Assert.Contains("OpenApiClientHelpers.AppendQueryParameter(pathBuilder, ref hasQuery, \"api_key\", _partnerApiKey);", source);
    }

    [Fact]
    public void BearerSecurityScheme_GeneratesAuthorizationHeaderInitialization()
    {
        const string openApi = """
            openapi: 3.0.1
            info:
              title: Bearer Security API
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
                            type: object
            components:
              securitySchemes:
                partner:
                  type: http
                  scheme: bearer
        """;

        var source = GenerateSource(openApi);

        Assert.Contains("public TestClient(string? partnerToken = default)", source);
        Assert.Contains("_httpClient.DefaultRequestHeaders.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue(\"Bearer\", partnerToken);", source);
    }

    [Fact]
    public void MultipleSecuritySchemes_GeneratesMultipleConstructorParameters()
    {
        const string openApi = """
            openapi: 3.0.1
            info:
              title: Multi Security API
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
                            type: object
            components:
              securitySchemes:
                oauth2:
                  type: oauth2
                  flows:
                    authorizationCode:
                      authorizationUrl: https://example.com/auth
                      tokenUrl: https://example.com/token
                partner:
                  type: apiKey
                  in: query
                  name: api_key
        """;

        var source = GenerateSource(openApi);

        Assert.Contains("accessToken", source);
        Assert.Contains("partnerApiKey", source);
        Assert.Contains("Authorization", source);
        Assert.Contains("api_key", source);
    }

    [Fact]
    public void HeaderApiKeySecurityScheme_GeneratesDefaultHeaderInitialization()
    {
        const string openApi = """
            openapi: 3.0.1
            info:
              title: Header Security API
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
                            type: object
            components:
              securitySchemes:
                partner:
                  type: apiKey
                  in: header
                  name: X-Partner-Key
            """;

        var source = GenerateSource(openApi);

        Assert.Contains("public TestClient(string? partnerApiKey = default)", source);
        Assert.Contains("_httpClient.DefaultRequestHeaders.TryAddWithoutValidation(\"X-Partner-Key\", partnerApiKey);", source);
    }

    [Fact]
    public void CookieApiKeySecurityScheme_GeneratesCookieHeaderInitialization()
    {
        const string openApi = """
            openapi: 3.0.1
            info:
              title: Cookie Security API
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
                            type: object
            components:
              securitySchemes:
                session:
                  type: apiKey
                  in: cookie
                  name: session_id
            """;

        var source = GenerateSource(openApi);

        Assert.Contains("public TestClient(string? sessionApiKey = default)", source);
        Assert.Contains("_httpClient.DefaultRequestHeaders.TryAddWithoutValidation(\"Cookie\", \"session_id=\" + sessionApiKey);", source);
    }

    [Fact]
    public void OpenApi32_BearerSecurityScheme_GeneratesAuthorizationHeader()
    {
        const string openApi = """
            openapi: 3.2.0
            info:
              title: Bearer Security API
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
                            type: object
            components:
              securitySchemes:
                partner:
                  type: http
                  scheme: bearer
        """;

        var source = GenerateSource(openApi);

        Assert.Contains("public TestClient(string? partnerToken = default)", source);
        Assert.Contains("_httpClient.DefaultRequestHeaders.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue(\"Bearer\", partnerToken);", source);
    }

    [Fact]
    public void OpenApi32_ApiKeySecuritySchemes_WorkCorrectly()
    {
        const string openApi = """
            openapi: 3.2.0
            info:
              title: APIKey Security API
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
                            type: object
            components:
              securitySchemes:
                queryKey:
                  type: apiKey
                  in: query
                  name: api_key
                headerKey:
                  type: apiKey
                  in: header
                  name: X-API-Key
                cookieKey:
                  type: apiKey
                  in: cookie
                  name: session_token
        """;

        var source = GenerateSource(openApi);

        Assert.Contains("queryKeyApiKey", source);
        Assert.Contains("headerKeyApiKey", source);
        Assert.Contains("cookieKeyApiKey", source);
        Assert.Contains("api_key", source);
        Assert.Contains("X-API-Key", source);
        Assert.Contains("session_token", source);
    }
}
