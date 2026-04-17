using System.Net;
using System.Net.Http.Headers;
using System.Reflection;
using System.Text;

using Xunit;

namespace OpenApiWeaver.Tests;

public sealed partial class ClientGeneratorTests
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
        Assert.Contains("private readonly string? _partnerToken;", source);
        Assert.Contains("_partnerToken = partnerToken;", source);
        Assert.Contains("request.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue(\"Bearer\", _partnerToken);", source);
        Assert.DoesNotContain("DefaultRequestHeaders.Authorization", source);
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
        Assert.Contains("private readonly string? _partnerApiKey;", source);
        Assert.Contains("request.Headers.TryAddWithoutValidation(\"X-Partner-Key\", _partnerApiKey);", source);
        Assert.DoesNotContain("DefaultRequestHeaders.TryAddWithoutValidation(\"X-Partner-Key\"", source);
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
        Assert.Contains("private readonly string? _sessionApiKey;", source);
        Assert.Contains("request.Headers.TryAddWithoutValidation(\"Cookie\", \"session_id=\" + _sessionApiKey);", source);
        Assert.DoesNotContain("DefaultRequestHeaders.TryAddWithoutValidation(\"Cookie\"", source);
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
        Assert.Contains("request.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue(\"Bearer\", _partnerToken);", source);
    }

    [Fact]
    public async Task InjectedHttpClient_PreservesBaseAddress_AndAppliesSecurityPerRequest()
    {
        const string openApi = """
            openapi: 3.0.1
            info:
              title: Injected HttpClient API
              version: v1
            servers:
              - url: https://api.example.com/v1
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
                bearer:
                  type: http
                  scheme: bearer
                partner:
                  type: apiKey
                  in: header
                  name: X-Partner-Key
                session:
                  type: apiKey
                  in: cookie
                  name: session_id
                queryKey:
                  type: apiKey
                  in: query
                  name: api_key
        """;

        using var generatedAssembly = LoadGeneratedAssembly(openApi);
        var clientType = Assert.Single(generatedAssembly.Assembly.GetTypes(), static type => type.Name == "TestClient");
        var handler = new RecordingHttpMessageHandler();
        using var httpClient = new HttpClient(handler);
        httpClient.BaseAddress = new Uri("https://override.example.com/custom/");

        var client = Activator.CreateInstance(clientType, httpClient, "bearer-token", "partner-key", "session-token", "query-value");
        Assert.NotNull(client);

        var operationProperty = Assert.Single(clientType.GetProperties(), static property => property.PropertyType.GetMethod("ListReportsAsync") is not null);
        var operationClient = operationProperty.GetValue(client);
        Assert.NotNull(operationClient);

        var operationMethod = operationClient.GetType().GetMethod("ListReportsAsync", BindingFlags.Instance | BindingFlags.Public);
        Assert.NotNull(operationMethod);

        var invocation = Assert.IsAssignableFrom<Task>(operationMethod.Invoke(operationClient, [CancellationToken.None]));
        await invocation;

        var request = Assert.Single(handler.Requests);
        Assert.Equal(new Uri("https://override.example.com/custom/reports?api_key=query-value"), request.RequestUri);
        Assert.Equal("Bearer", request.Authorization?.Scheme);
        Assert.Equal("bearer-token", request.Authorization?.Parameter);
        Assert.Equal(["partner-key"], Assert.Contains("X-Partner-Key", request.Headers));
        Assert.Equal(["session_id=session-token"], Assert.Contains("Cookie", request.Headers));
        Assert.Null(httpClient.DefaultRequestHeaders.Authorization);
        Assert.False(httpClient.DefaultRequestHeaders.Contains("X-Partner-Key"));
        Assert.False(httpClient.DefaultRequestHeaders.Contains("Cookie"));

        ((IDisposable)client).Dispose();

        using var response = await httpClient.GetAsync("health", TestContext.Current.CancellationToken);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public void HttpClientConstructorOverload_IsGenerated()
    {
        const string openApi = """
            openapi: 3.0.1
            info:
              title: Injected HttpClient API
              version: v1
            servers:
              - url: https://api.example.com/v1
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
        """;

        var source = GenerateSource(openApi);

        Assert.Contains("private readonly bool _ownsHttpClient;", source);
        Assert.Contains("public TestClient()", source);
        Assert.Contains("public TestClient(HttpClient httpClient)", source);
        Assert.Contains("private TestClient(HttpClient httpClient, bool ownsHttpClient)", source);
        Assert.Contains("if (_httpClient.BaseAddress is null)", source);
        Assert.Contains("if (_ownsHttpClient)", source);
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

    private sealed record CapturedHttpRequest(Uri? RequestUri, AuthenticationHeaderValue? Authorization, IReadOnlyDictionary<string, string[]> Headers);

    private sealed class RecordingHttpMessageHandler : HttpMessageHandler
    {
        private readonly List<CapturedHttpRequest> _requests = [];

        public IReadOnlyList<CapturedHttpRequest> Requests => _requests;

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var headers = request.Headers.ToDictionary(
                static header => header.Key,
                static header => header.Value.ToArray(),
                StringComparer.OrdinalIgnoreCase);
            _requests.Add(new CapturedHttpRequest(request.RequestUri, request.Headers.Authorization, headers));

            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent("{}", Encoding.UTF8, "application/json")
            });
        }
    }
}
