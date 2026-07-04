# Features

## Comparison with other generators

How OpenApiWeaver compares to other popular OpenAPI-to-C# client generators:

| | OpenApiWeaver | Kiota | NSwag | Refitter |
|---|---|---|---|---|
| Mechanism | Roslyn incremental source generator | CLI (`kiota generate`) | CLI / MSBuild | Refit interface generator (source generator / CLI) |
| Code generation | Automatic on build, incremental | Manual re-run / `kiota update` | Manual or MSBuild | Automatic (source generator) |
| Generated files in repo | Not needed (emitted in-memory) | Committed to the repo | Committed to the repo | Not needed (source generator) |
| Runtime dependencies | None (plain `HttpClient`) | `Microsoft.Kiota.*` packages | JSON library (e.g. Newtonsoft.Json) | Refit runtime |
| Runtime reflection | None | None | None | None (Refit source generator) |
| Distribution | Single NuGet (`PrivateAssets="all"`) | dotnet tool + runtime packages | dotnet tool / NuGet | NuGet + Refit |
| Languages | C# | C#, Java, Go, TypeScript, Python, … | C#, TypeScript | C# |

**Choose OpenApiWeaver when** you want clients to regenerate automatically on every build with no committed code, no runtime dependencies, and a single NuGet package — without a separate CLI step or generated files to maintain. For multi-language generation or a large, mature ecosystem, Kiota and NSwag remain strong choices.

## Incremental source generation

OpenApiWeaver uses the Roslyn incremental generator pipeline for efficient, cached rebuilds. Only changed documents are reprocessed, which keeps builds efficient even in large solutions with multiple OpenAPI files.

## JSON & YAML support

OpenApiWeaver reads OpenAPI 3.0-3.2 documents in the following formats:

- `.json` - OpenAPI 3.x JSON
- `.yaml` / `.yml` - OpenAPI 3.x YAML

## Tag-based sub-clients

Operations are grouped by OpenAPI tags and exposed as properties on the root client. Each tag becomes a separate sub-client class:

```csharp
var client = new PetstoreClient(accessToken: "token");

// "Pets" tag -> client.Pets property
var pet = await client.Pets.GetAsync(petId: 1);

// "Users" tag -> client.Users property
var user = await client.Users.GetAsync(userId: "me");
```

Method names are derived from `operationId` when available, with an `Async` suffix appended automatically.

When a tag includes a description, OpenApiWeaver emits it as XML documentation on the generated tag property and sub-client class.

## Typed request / response models

OpenApiWeaver generates classes for object schemas and maps them to strongly typed method parameters and return values:

- **Object schemas** -> `sealed class` with `[JsonPropertyName]` attributes
- **Polymorphic object schemas** -> base class plus derived classes when a schema uses `discriminator` with `oneOf`
- **Enums** -> `readonly record struct` with a dedicated `JsonConverter` for string enums, or standard `enum` for integer enums; see [Schema Type Mapping](./schema-types)
- **Arrays** -> `IReadOnlyList<T>`
- **Dictionaries** (`additionalProperties` / `patternProperties`) -> `IReadOnlyDictionary<string, T>`
- **Inline object / enum schemas** -> nested types under the owning model class

Naming conventions are converted to idiomatic C# names, such as `snake_case` to `PascalCase`, while the original JSON names are preserved through `[JsonPropertyName]`.

Optional (non-required) nullable properties are annotated with `[JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]` so they are omitted from the JSON payload when `null`.

OpenAPI 3.2 nullable type arrays such as `type: ["string", "null"]` are mapped to nullable CLR types like `string?`.

When a component schema uses `discriminator` together with `oneOf` references to named component schemas, OpenApiWeaver generates a polymorphic base model annotated with `[JsonPolymorphic]` and `[JsonDerivedType]`, and the referenced schemas become derived classes.

## Inline / nested schemas

Inline object or enum schemas that appear inside property definitions, array `items`, or `additionalProperties` are emitted as nested types under the owning generated model class. This keeps the type hierarchy compact and avoids excessively long top-level type names.

```csharp
public sealed class Order
{
    [JsonPropertyName("status")]
    public Order.StatusEnum? Status { get; init; }

    // Inline enum schema generated as a nested type
    public readonly record struct StatusEnum(string Value)
    {
        public static readonly StatusEnum Placed = new("placed");
        public static readonly StatusEnum Approved = new("approved");
    }
}
```

## Polymorphic schemas with discriminator

OpenApiWeaver supports OpenAPI discriminator-based polymorphism for component schemas that follow this pattern:

- The base schema declares `discriminator.propertyName`
- The base schema uses `oneOf`
- Every `oneOf` member is a `$ref` to a named schema in `components/schemas`

The generated base type becomes a non-sealed class annotated for System.Text.Json polymorphism, and each referenced schema is emitted as a derived type. If `discriminator.mapping` is present, the mapping key is used as the discriminator value. Otherwise the referenced schema name is used.

```yaml
animal:
    type: object
    discriminator:
        propertyName: kind
        mapping:
            dog: '#/components/schemas/dog'
            cat: '#/components/schemas/cat'
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
```

```csharp
[JsonPolymorphic(TypeDiscriminatorPropertyName = "kind")]
[JsonDerivedType(typeof(Dog), typeDiscriminator: "dog")]
[JsonDerivedType(typeof(Cat), typeDiscriminator: "cat")]
public class Animal
{
    [JsonPropertyName("name")]
    public required string Name { get; init; }
}

public sealed class Dog : Animal
{
    [JsonPropertyName("barks")]
    public required bool Barks { get; init; }
}
```

The discriminator property itself is not generated as a normal CLR property on the base or derived types, which avoids duplicate JSON output during polymorphic serialization.

Unsupported discriminator patterns fail generation with `OAW006`. This includes `discriminator` used with `anyOf`, `discriminator` without `oneOf`, inline `oneOf` members, duplicate discriminator values, and mappings that reference schemas not listed in `oneOf`.

## Response types

The generated methods return different types based on the response content:

| Response Content | Return Type |
|---|---|
| `application/json` | `Task<T>` with the deserialized response type |
| `text/plain` or similar | `Task<string>` |
| Binary content | `Task<byte[]>` |
| No content (e.g. 204) | `Task` |

When multiple successful responses are available, OpenApiWeaver prefers a response with a body over a lower-status no-content response. When multiple response media types are available, JSON is preferred first, followed by binary content and then text.

## Runtime error handling

Generated clients do not call `EnsureSuccessStatusCode()`. Instead, they emit explicit error handling code for non-success responses so that OpenAPI error definitions remain available to the caller.

- Non-2xx responses throw `OpenApiException`
- The exception includes `StatusCode`, `ReasonPhrase`, `ContentType`, and the raw `ResponseContent`
- If the OpenAPI operation defines a matching error response schema, the generated code throws `OpenApiException<TError>` with the deserialized error payload exposed through `Error`
- Matching prefers exact status codes first, then wildcard patterns such as `4XX`, then `default`

For JSON error responses, deserialization is attempted only when the response content type is JSON-compatible. If deserialization fails, the client falls back to the non-generic `OpenApiException` and preserves the raw response body for troubleshooting.

For successful JSON responses that are expected to contain a body, an empty response body results in `InvalidOperationException("The response body was empty.")`.

Example:

```csharp
try
{
    await client.Partners.CreatePartnerAsync(body, cancellationToken);
}
catch (OpenApiException<ValidationProblem> exception) when (exception.StatusCode == 400)
{
    Console.WriteLine($"Validation failed: {exception.Error?.Message}");
}
catch (OpenApiException exception)
{
    Console.WriteLine($"Request failed: {exception.StatusCode} {exception.ReasonPhrase}");
    Console.WriteLine($"Content-Type: {exception.ContentType}");
    Console.WriteLine(exception.ResponseContent);
}
```

## Multiple request body formats

OpenApiWeaver supports the following content types for request bodies:

| Content Type | Serialization |
|---|---|
| `application/json` | `JsonSerializer` with `JsonSerializerDefaults.Web` |
| `application/x-www-form-urlencoded` | `FormUrlEncodedContent` |
| `multipart/form-data` | `MultipartFormDataContent` (supports binary file uploads) |

If a request body declares multiple supported media types, JSON is used when available.

### Compile-time-only policy for form / multipart

For `application/x-www-form-urlencoded` and `multipart/form-data` request bodies, all required code must be generated entirely at compile time. If that is not possible, generation fails with `OAW005`.

Supported form and multipart request bodies require:

- A schema reference to `components/schemas`
- An object shape whose properties are known at generation time
- Property types that map directly to CLR types (scalars, `byte[]`, supported collections)

The following features are intentionally unsupported for form and multipart request bodies:

- Inline request body schemas
- `oneOf` / `anyOf`
- `additionalProperties` / `patternProperties`

## Parameter locations

Parameters defined in the OpenAPI document are mapped to method parameters according to their location:

| Location | Behavior |
|---|---|
| `path` | Interpolated into the URL path |
| `query` | Appended as query string parameters |
| `header` | Added as request headers |
| `cookie` | Added to the `Cookie` header |

Array parameters use OpenAPI-compatible defaults: query arrays are emitted as repeated query parameters, while path, header, and cookie arrays are formatted as comma-separated values.

## Security scheme support

Constructor parameters are generated automatically from the security schemes defined in the OpenAPI document:

| Scheme | Generated Parameter |
|---|---|
| OAuth2 / Bearer token | `string accessToken` - sent as `Authorization: Bearer {token}` |
| API key (header) | `string apiKey` - sent as a custom request header |
| API key (query) | `string apiKey` - appended to the query string |
| API key (cookie) | `string apiKey` - sent in the `Cookie` header |

Multiple security schemes can be combined. For example, if an API requires both an OAuth2 token and an API key, the constructor accepts both parameters.

Security values are stored on the generated client and applied to each generated `HttpRequestMessage` according to the OpenAPI security requirements. Operation-level `security` overrides document-level security, and `security: []` sends no credentials for that operation. Documents that define schemes but no explicit security requirements retain the existing behavior of applying configured credentials to generated requests.

Credentials are applied per request rather than by mutating `HttpClient.DefaultRequestHeaders`. This includes header, query, cookie, OAuth2, and bearer token schemes, and keeps injected `HttpClient` instances free from shared authentication state.

## All HTTP methods

OpenApiWeaver supports all standard HTTP methods: GET, POST, PUT, DELETE, PATCH, HEAD, OPTIONS, and TRACE.

## Build-time diagnostics

OpenApiWeaver reports errors and warnings as standard compiler diagnostics during compilation:

| Code | Severity | Description |
|---|---|---|
| OAW001 | Error | OpenAPI document is empty |
| OAW002 | Warning | OpenAPI document has validation warnings |
| OAW003 | Error | OpenAPI document is invalid (e.g. malformed JSON/YAML) |
| OAW004 | Error | OpenAPI document uses an unsupported feature |
| OAW005 | Error | OpenAPI request body uses an unsupported feature |
| OAW006 | Error | OpenAPI discriminator uses an unsupported feature |
| OAW007 | Error | OpenAPI schema uses an unsupported feature |

These diagnostics appear in the Visual Studio Error List, `dotnet build` output, and CI logs, just like any other compiler diagnostic.

## Generated client extensibility

The root client class is generated as a `partial class`, so you can extend it in a separate file without modifying the generated code.

The root client also implements `IDisposable`. If it creates the `HttpClient` internally, `Dispose()` releases it. If you inject an existing `HttpClient`, the generated client does not dispose it.

## XML documentation comments

The generated code includes `<summary>`, `<remarks>`, `<param>`, and `<returns>` XML documentation comments derived from `info`, tag descriptions, operation `summary` and `description`, parameter descriptions, response `summary` and `description`, and schema `title` and `description`. HTML markup is stripped automatically so generated IntelliSense remains clean.
