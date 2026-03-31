# How It Works

## Generation pipeline

For each OpenAPI document included as an `OpenApiWeaverDocument` item, the generator performs the following steps:

1. **Parse** - reads the document with [Microsoft.OpenApi](https://github.com/microsoft/OpenAPI.NET), supporting both JSON and YAML formats
2. **Transform** - derives class and method names, converts naming conventions (e.g. `snake_case` -> `PascalCase`), resolves `$ref` references, and classifies schemas
3. **Group** - organizes operations by their OpenAPI tags into sub-client classes
4. **Emit schemas** - generates sealed classes for object schemas, `readonly record struct` with `JsonConverter` for string enums, standard `enum` for integer enums, and maps primitive / collection types. Inline schemas become nested types.
5. **Emit clients** - generates async methods for each operation, with the correct request body serialization and response deserialization

## Generated client structure

The root client class:

- Creates an internal `HttpClient` with `BaseAddress` set from the first OpenAPI `servers` entry
- Accepts optional security credentials (bearer tokens, API keys) via constructor parameters
- Exposes one property per tag group (e.g. `client.Pets`, `client.Users`)
- Is generated as a `partial class` so you can extend it in a separate file
- Implements `IDisposable` to release the underlying `HttpClient`

Each tag sub-client contains the async operation methods, with parameters mapped from path, query, header, and cookie parameters defined in the OpenAPI document.

## Naming conventions

The generator automatically converts naming conventions to be idiomatic in C#:

- Schema names and property names are converted from `snake_case` to `PascalCase`
- Method names are derived from `operationId` when available, with an `Async` suffix appended
- Original JSON property names are preserved via `[JsonPropertyName]` attributes

## Incremental generation

OpenApiWeaver leverages the Roslyn incremental generator pipeline for fast, cached rebuilds. Only the documents that have changed are re-processed, making it efficient even in large solutions with multiple OpenAPI files.
