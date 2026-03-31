# Features

## Incremental source generation

OpenApiWeaver leverages the Roslyn incremental generator pipeline for fast, cached rebuilds. Only the documents that have changed are re-processed, making it efficient even in large solutions with multiple OpenAPI files.

## JSON & YAML support

Reads OpenAPI 3.x documents in the following formats:

- `.json` — OpenAPI 3.x JSON
- `.yaml` / `.yml` — OpenAPI 3.x YAML

## Tag-based sub-clients

Operations are grouped by their OpenAPI tags and exposed as properties on the root client. Each tag becomes a separate sub-client class:

```csharp
var client = new PetstoreClient(accessToken: "token");

// "Pets" tag → client.Pets property
var pet = await client.Pets.GetAsync(petId: 1);

// "Users" tag → client.Users property
var user = await client.Users.GetAsync(userId: "me");
```

Method names are derived from `operationId` when available, with an `Async` suffix appended automatically.

## Typed request / response models

Generates sealed classes for object schemas and maps them to strongly typed method parameters and return values:

- **Object schemas** → `sealed class` with `[JsonPropertyName]` attributes
- **Enums** → `readonly record struct` with static members (see [Schema Type Mapping](./schema-types))
- **Arrays** → `IReadOnlyList<T>`
- **Dictionaries** (`additionalProperties`) → `IReadOnlyDictionary<string, T>`

Naming conventions are automatically converted from `snake_case` to `PascalCase` for C# idiomatic use, while preserving the original JSON names via `[JsonPropertyName]`.

## Response types

The generated methods return different types based on the response content:

| Response Content | Return Type |
|---|---|
| `application/json` | `Task<T>` with the deserialized response type |
| `text/plain` or similar | `Task<string>` |
| Binary content | `Task<byte[]>` |
| No content (e.g. 204) | `Task` |

## Multiple request body formats

Supports the following content types for request bodies:

| Content Type | Serialization |
|---|---|
| `application/json` | `JsonSerializer` with `JsonSerializerDefaults.Web` |
| `application/x-www-form-urlencoded` | `FormUrlEncodedContent` |
| `multipart/form-data` | `MultipartFormDataContent` (supports binary file uploads) |

## Parameter locations

Parameters defined in the OpenAPI document are mapped to method parameters based on their location:

| Location | Behavior |
|---|---|
| `path` | Interpolated into the URL path |
| `query` | Appended as query string parameters |
| `header` | Added as request headers |
| `cookie` | Added to the `Cookie` header |

## Security scheme support

Constructor parameters are automatically generated based on the security schemes defined in the OpenAPI document:

| Scheme | Generated Parameter |
|---|---|
| OAuth2 / Bearer token | `string accessToken` — sent as `Authorization: Bearer {token}` |
| API key (header) | `string apiKey` — sent as a custom request header |
| API key (query) | `string apiKey` — appended to the query string |
| API key (cookie) | `string apiKey` — sent in the `Cookie` header |

Multiple security schemes can be combined. For example, if an API requires both an OAuth2 token and an API key, the constructor will accept both parameters.

## All HTTP methods

Supports all standard HTTP methods: GET, POST, PUT, DELETE, PATCH, HEAD, OPTIONS, and TRACE.

## Build-time diagnostics

Reports errors and warnings as standard compiler diagnostics during compilation:

| Code | Severity | Description |
|---|---|---|
| OAW001 | Error | OpenAPI document is empty |
| OAW002 | Warning | OpenAPI document has validation warnings |
| OAW003 | Error | OpenAPI document is invalid (e.g. malformed JSON/YAML) |
| OAW004 | Error | OpenAPI document uses an unsupported feature |

These diagnostics appear in the Visual Studio Error List, `dotnet build` output, and CI logs, just like any other compiler diagnostic.
