# Features

## Incremental source generation

OpenApiWeaver leverages the Roslyn incremental generator pipeline for fast, cached rebuilds. Only the files that have changed are re-processed, making it efficient even in large solutions.

## JSON & YAML support

Reads `.json`, `.yaml`, and `.yml` OpenAPI documents.

## Tag-based sub-clients

Operations are grouped by OpenAPI tags and exposed as properties on the root client:

```csharp
var client = new PetstoreClient(accessToken: "token");

// Access operations grouped by tag
var pets = await client.Pets.ListAsync();
```

## Typed request / response models

Generates sealed classes, enums (as `readonly record struct`), and collection types from `components/schemas`.

## Multiple request body formats

Supports the following content types:

- `application/json`
- `application/x-www-form-urlencoded`
- `multipart/form-data`

## Security scheme initialization

Constructor parameters are generated for OAuth2 / Bearer tokens, API keys (header, query, cookie) based on the security schemes defined in the OpenAPI document.

## All HTTP methods

Supports GET, POST, PUT, DELETE, PATCH, HEAD, OPTIONS, and TRACE.

## Build-time diagnostics

Reports errors and warnings for invalid or unsupported OpenAPI documents during compilation.
