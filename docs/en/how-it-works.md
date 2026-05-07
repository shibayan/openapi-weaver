# How It Works

## Generation pipeline

For each OpenAPI document included as an `OpenApiWeaverDocument` item, the generator performs the following steps:

1. **Project inputs** - The bundled `buildTransitive` targets project each `OpenApiWeaverDocument` item into compiler-visible additional files and forward metadata such as `ClientName` and `Namespace`.
2. **Parse** - The document is read with [Microsoft.OpenApi](https://github.com/microsoft/OpenAPI.NET), with support for OpenAPI 3.0-3.2 in both JSON and YAML formats.
3. **Transform** - The generator derives class and method names, converts naming conventions such as `snake_case` to `PascalCase`, resolves `$ref` references, classifies schemas, and sanitizes OpenAPI HTML content for XML documentation output.
4. **Group** - Operations are organized by OpenAPI tags into sub-client classes, and tag descriptions are carried into generated XML documentation.
5. **Emit schemas** - The generator emits sealed classes for object schemas, `readonly record struct` types with `JsonConverter` for string enums, standard `enum` types for integer enums, and mapped primitive and collection types. Inline schemas become nested types.
6. **Emit clients** - The generator emits asynchronous methods for each operation, including request body serialization, response deserialization, and XML documentation comments derived from OpenAPI metadata.

During client emission, OpenApiWeaver also generates runtime error handling paths. Non-success responses capture status, headers, and raw content, then throw `OpenApiException` or `OpenApiException<TError>` when a matching typed error schema is defined for that status code.

## Generated client structure

The root client class:

- Creates an internal `HttpClient`, or accepts one through constructor injection, with `BaseAddress` populated from the first OpenAPI `servers` entry when needed
- Accepts optional security credentials such as bearer tokens and API keys through constructor parameters
- Exposes one property per tag group, for example `client.Pets` and `client.Users`
- Emits XML documentation for the client itself and for each tag property from `info`, `tags`, `summary`, `description`, and response metadata
- Is generated as a `partial class`, which allows extension in a separate file
- Implements `IDisposable` so the underlying `HttpClient` is released only when the generated client owns it

Each tag sub-client contains asynchronous operation methods, with parameters mapped from the path, query, header, and cookie parameters defined in the OpenAPI document. Array parameters use repeated query keys for query parameters and comma-separated values for path, header, and cookie parameters.

Security schemes are stored on the generated client and applied to each `HttpRequestMessage` according to document-level or operation-level security requirements. This keeps authentication behavior consistent across query, header, cookie, OAuth2, and bearer token schemes, and avoids mutating `HttpClient.DefaultRequestHeaders`.

Each generated operation also includes explicit non-success handling instead of relying on `HttpResponseMessage.EnsureSuccessStatusCode()`, which allows typed OpenAPI error payloads to flow through exceptions.

## Naming conventions

The generator converts naming conventions to idiomatic C# forms:

- Schema names and property names are converted from `snake_case` to `PascalCase`
- Method names are derived from `operationId` when available, with an `Async` suffix appended
- Original JSON property names are preserved via `[JsonPropertyName]` attributes

## Incremental generation

OpenApiWeaver uses the Roslyn incremental generator pipeline for efficient, cached rebuilds. Only changed documents are reprocessed, which keeps builds efficient even in large solutions with multiple OpenAPI files.
