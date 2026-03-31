# OpenApiWeaver

[![NuGet](https://img.shields.io/nuget/v/OpenApiWeaver)](https://www.nuget.org/packages/OpenApiWeaver)
[![License](https://img.shields.io/github/license/shibayan/openapi-weaver)](LICENSE)

**OpenApiWeaver** is an incremental Roslyn source generator that turns OpenAPI 3.x documents into strongly typed C# HTTP clients at build time. No runtime code generation, no reflection — just plain C# emitted during compilation.

> **Requires .NET 8.0 or later.**

## Quick Start

### 1. Install the package

```xml
<ItemGroup>
  <PackageReference Include="OpenApiWeaver" Version="x.y.z" PrivateAssets="all" />
</ItemGroup>
```

`PrivateAssets="all"` ensures the source generator is used only at build time and is not exposed as a transitive dependency.

### 2. Add your OpenAPI document

```xml
<ItemGroup>
  <OpenApiWeaverDocument Include="openapi\petstore.yaml"
                         ClientName="PetstoreClient"
                         Namespace="Contoso.Generated" />
</ItemGroup>
```

| Metadata | Required | Default |
|---|---|---|
| `ClientName` | No | Derived from file name (`petstore.yaml` → `PetstoreClient`) |
| `Namespace` | No | Project's `RootNamespace` |

### 3. Use the generated client

```csharp
var client = new PetstoreClient(accessToken: "your-token");

// Operations are grouped by OpenAPI tag
var pet = await client.Pets.GetAsync(petId: 1);
```

No extra dependencies are required — the package bundles the source generator and all analyzer assemblies.

## Features

- **Incremental source generation** — leverages the Roslyn incremental generator pipeline for fast, cached rebuilds
- **JSON & YAML support** — reads `.json`, `.yaml`, and `.yml` OpenAPI 3.x documents
- **Tag-based sub-clients** — operations are grouped by OpenAPI tags and exposed as properties on the root client
- **Typed request / response models** — generates sealed classes, enums (C# `enum` for integer-valued schemas, or `readonly record struct` wrappers for others), and collection types from `components/schemas`
- **Multiple request body formats** — `application/json`, `application/x-www-form-urlencoded`, and `multipart/form-data`
- **Security scheme support** — constructor parameters for OAuth2 / Bearer tokens, API keys (header, query, cookie)
- **All HTTP methods** — GET, POST, PUT, DELETE, PATCH, HEAD, OPTIONS, TRACE
- **Build-time diagnostics** — reports errors and warnings during compilation (see [Diagnostics](#diagnostics))

## How It Works

For each OpenAPI document included as an `OpenApiWeaverDocument` item, the generator:

1. **Parses** the document with [Microsoft.OpenApi](https://github.com/microsoft/OpenAPI.NET)
2. **Transforms** the parsed model — derives class names, normalizes naming conventions (snake_case → PascalCase), and resolves schemas
3. **Groups** operations by OpenAPI tags into sub-client classes
4. **Emits** request / response DTOs from component schemas
5. **Generates** async methods for each operation, using `operationId` as the method name when available

The generated root client:

- Creates an internal `HttpClient` with `BaseAddress` set from the first OpenAPI `servers` entry
- Accepts optional security credentials (bearer tokens, API keys) via constructor parameters
- Exposes one property per tag group (e.g. `client.Pets`, `client.Users`)

## Diagnostics

OpenApiWeaver reports the following diagnostics during compilation:

| Code | Severity | Description |
|---|---|---|
| OAW001 | Error | OpenAPI document is empty |
| OAW002 | Warning | OpenAPI document has validation warnings |
| OAW003 | Error | OpenAPI document is invalid |
| OAW004 | Error | OpenAPI document uses an unsupported feature |

## Documentation

For detailed guides, configuration options, and schema type mapping, visit the [documentation site](https://shibayan.github.io/openapi-weaver/).

## License

This project is licensed under the [MIT License](LICENSE).

Properties use `[JsonPropertyName]` attributes for correct serialization of snake_case or other non-PascalCase field names.

Inline object / enum schemas are emitted as nested types under the owning generated model so property paths do not expand into excessively long top-level type names.

## Security Schemes

| Scheme | Location | Behavior |
|---|---|---|
| OAuth2 / Bearer | `Authorization` header | `Bearer {token}` |
| API Key | Header | Custom header via `DefaultRequestHeaders` |
| API Key | Query | Appended to request URL |
| API Key | Cookie | `Cookie` header with `name=value` |

All security parameters are optional in the generated constructor.

## Compile-Time-Only Request Body Policy

`OpenApiWeaver` does not use runtime reflection or fallback paths for form-based request bodies. If `application/x-www-form-urlencoded` or `multipart/form-data` content cannot be emitted entirely at compile time, generation fails with `OAW004`.

Supported form and multipart request bodies require:

- A schema reference to `components/schemas`
- An object shape whose properties are known at generation time
- Property types that map directly to CLR types (scalars, `byte[]`, supported collections)

The following are **intentionally unsupported** for form/multipart request bodies:

- Inline request body schemas
- `oneOf` / `anyOf`
- `additionalProperties` / `patternProperties`

## Diagnostics

| Rule ID | Severity | Description |
|---|---|---|
| OAW001 | Error | OpenAPI document is empty |
| OAW002 | Warning | OpenAPI document has validation warnings |
| OAW003 | Error | OpenAPI document is invalid or could not be parsed |
| OAW004 | Error | OpenAPI document uses an unsupported feature |

## Repository Layout

```
src/OpenApiWeaver/          # Source generator implementation
tests/OpenApiWeaver.Tests/  # xUnit tests
samples/SampleApp/          # Minimal sample project
```

## Requirements

- .NET SDK 8.0 or later

## License

This project is licensed under the MIT License. See [LICENSE](LICENSE) for details.
