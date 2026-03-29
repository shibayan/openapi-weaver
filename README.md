# OpenApiWeaver

[![NuGet](https://img.shields.io/nuget/v/OpenApiWeaver)](https://www.nuget.org/packages/OpenApiWeaver)
[![License](https://img.shields.io/github/license/shibayan/openapi-weaver)](LICENSE)

**OpenApiWeaver** is an incremental Roslyn source generator that turns OpenAPI documents into strongly typed C# HTTP clients at build time. No runtime code generation, no reflection — just plain C# emitted during compilation.

## Quick Start

### 1. Install the package

```xml
<ItemGroup>
  <PackageReference Include="OpenApiWeaver" Version="x.y.z" PrivateAssets="all" />
</ItemGroup>
```

### 2. Add your OpenAPI document

Preferred configuration:

```xml
<ItemGroup>
  <OpenApiWeaverDocument Include="openapi\petstore.yaml"
                         ClientName="PetstoreSdk"
                         Namespace="Contoso.Generated" />
</ItemGroup>
```

`ClientName` and `Namespace` are optional metadata. If omitted, the generator falls back to the file name and the project's `RootNamespace`.

`AdditionalFiles` is still supported for simple scenarios:

```xml
<ItemGroup>
  <AdditionalFiles Include="openapi\petstore.yaml" />
</ItemGroup>
```

The package bundles the source generator and all required analyzer dependencies — no extra references needed.

### 3. Use the generated client

```csharp
var client = new PetstoreClient(accessToken: "your-token");

var pets = await client.Pets.ListAsync();
```

The client name is derived from the file name (`petstore.yaml` → `PetstoreClient`).

## Features

- **Incremental source generation** — leverages the Roslyn incremental generator pipeline for fast, cached rebuilds
- **JSON & YAML support** — reads `.json`, `.yaml`, and `.yml` OpenAPI documents
- **Tag-based sub-clients** — operations are grouped by OpenAPI tags and exposed as properties on the root client
- **Typed request / response models** — generates sealed classes, enums (as `readonly record struct`), and collection types from `components/schemas`
- **Multiple request body formats** — `application/json`, `application/x-www-form-urlencoded`, and `multipart/form-data`
- **Security scheme initialization** — constructor parameters for OAuth2 / Bearer tokens, API keys (header, query, cookie)
- **All HTTP methods** — GET, POST, PUT, DELETE, PATCH, HEAD, OPTIONS, TRACE
- **Build-time diagnostics** — reports errors and warnings for invalid or unsupported OpenAPI documents

## How It Works

For each OpenAPI document included as an `OpenApiWeaverDocument` or `AdditionalFiles` item, the generator:

1. Parses the document with [Microsoft.OpenApi](https://github.com/microsoft/OpenAPI.NET)
2. Derives a client class name from the file name (e.g. `api-schema.json` → `ApiSchemaClient`)
3. Groups operations by OpenAPI tags into sub-client classes
4. Emits request / response DTOs from component schemas
5. Generates async methods for each operation, using `operationId` as the method name when available

The root client:

- Creates an internal `HttpClient` with `BaseAddress` set from the first OpenAPI `servers` entry
- Accepts optional security credentials (bearer tokens, API keys) via constructor parameters
- Exposes one property per tag group

## Supported Schema Types

| OpenAPI Type | C# Type |
|---|---|
| `integer` | `int` |
| `integer` (int64) | `long` |
| `number` (float) | `float` |
| `number` (double) | `double` |
| `boolean` | `bool` |
| `string` | `string` |
| `string` (date) | `DateOnly` |
| `string` (date-time) | `DateTimeOffset` |
| `string` (uuid) | `Guid` |
| `string` (binary) | `byte[]` |
| `array` | `IReadOnlyList<T>` |
| `object` with `additionalProperties` | `IReadOnlyDictionary<string, T>` |
| `allOf` | Flattened into a single class |
| `oneOf` / `anyOf` | Union-style nullable properties |
| `enum` | `readonly record struct` with static members |

Properties use `[JsonPropertyName]` attributes for correct serialization of snake_case or other non-PascalCase field names.

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
src/OpenApiWeaver/          # Source generator implementation (targets netstandard2.0)
tests/OpenApiWeaver.Tests/  # xUnit tests
samples/SampleApp/          # Minimal sample project
```

## Requirements

- .NET SDK 10.0+ (for the sample app and tests)
- Any C# project that supports Roslyn analyzers / source generators

The generator itself targets **netstandard2.0** and works with any compatible SDK.

## License

This project is licensed under the MIT License. See [LICENSE](LICENSE) for details.

## Generated Behavior

Current test coverage verifies the following behavior:

- Empty documents report an error diagnostic and generate no source
- Invalid documents report an error diagnostic and generate no source
- Unsupported compile-time-only request body shapes report `OAW004` and generate no source
- Multipart request bodies generate `byte[]` properties for binary fields and inline multipart content construction
- Optional form bodies generate nullable method parameters and conditional form content assignment
- No-content success responses generate non-generic `Task`
- Binary responses generate `Task<byte[]>`
- Snake_case schema properties are preserved with `JsonPropertyName`
- Mixed JSON and form request bodies prefer JSON when both are available
- Plain text responses generate `Task<string>`

## Build

```bash
dotnet build OpenApiWeaver.slnx
```

## Test

```bash
dotnet test OpenApiWeaver.slnx
```

## Sample App

```bash
dotnet run --project samples/SampleApp/SampleApp.csproj
```

The sample project enables emitted generated files under `samples/SampleApp/obj/Generated` so you can inspect the generated client code during development.

## Current Status

This repository contains the source generator, tests, and a local sample app. The sample app keeps a project-to-project analyzer reference for repository development, but consuming applications should use the `OpenApiWeaver` NuGet package.
