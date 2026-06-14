![OpenApiWeaver logo](https://raw.githubusercontent.com/shibayan/openapi-weaver/master/docs/public/logo.png)

[![CI](https://github.com/shibayan/openapi-weaver/actions/workflows/ci.yml/badge.svg)](https://github.com/shibayan/openapi-weaver/actions/workflows/ci.yml)
[![Downloads](https://badgen.net/nuget/dt/OpenApiWeaver)](https://www.nuget.org/packages/OpenApiWeaver/)
[![NuGet](https://badgen.net/nuget/v/OpenApiWeaver)](https://www.nuget.org/packages/OpenApiWeaver)
[![License](https://badgen.net/github/license/shibayan/openapi-weaver)](https://github.com/shibayan/openapi-weaver/blob/master/LICENSE)

**OpenApiWeaver** is an incremental Roslyn source generator that generates strongly typed C# HTTP clients from OpenAPI 3.x documents, including OpenAPI 3.2, at build time. It does not rely on runtime code generation or reflection, and emits plain C# during compilation.

## Quick Start

**1. Install the package**

```xml
<ItemGroup>
  <PackageReference Include="OpenApiWeaver" Version="x.y.z" PrivateAssets="all" />
</ItemGroup>
```

**2. Add your OpenAPI document**

```xml
<ItemGroup>
  <OpenApiWeaverDocument Include="openapi\petstore.yaml"
                         ClientName="PetstoreClient"
                         Namespace="Contoso.Generated" />
</ItemGroup>
```

Declare the document as an `OpenApiWeaverDocument` item rather than `AdditionalFiles`. The package's MSBuild targets project these items into compiler inputs automatically.

**3. Use the generated client**

```csharp
var client = new PetstoreClient(accessToken: "your-token");

// Operations are grouped by OpenAPI tag
var pet = await client.Pets.GetAsync(petId: 1);
```

You can also pass an existing `HttpClient` to the generated root client. When you do, the generated client reuses that instance, preserves an existing `BaseAddress`, and applies security credentials per request according to document-level or operation-level security requirements instead of mutating `DefaultRequestHeaders`.

The package includes the source generator and all required analyzer assemblies, so no additional dependencies are required.

## Features

- **Incremental source generation** — uses the Roslyn incremental generator pipeline to keep rebuilds efficient
- **OpenAPI 3.0-3.2 support** — reads `.json`, `.yaml`, and `.yml` documents, including OpenAPI 3.2 features such as response summaries and nullable type arrays
- **Tag-based sub-clients** — groups operations by OpenAPI tags and exposes them as properties on the root client
- **Typed request / response models** — generates classes, enums, nested inline types, dictionaries, and composition-aware schema mappings from `components/schemas`, including discriminator-based polymorphic models
- **Multiple request body formats** — supports `application/json`, `application/x-www-form-urlencoded`, and `multipart/form-data`
- **Security scheme support** — supports OAuth2 and bearer tokens, together with API keys in headers, query strings, and cookies, while honoring operation-level security requirements
- **Runtime error handling** — throws `OpenApiException`, with typed `OpenApiException<TError>` when error schemas are available
- **OpenAPI-driven XML docs** — generates IntelliSense comments from document, tag, operation, response, and schema metadata, with HTML removed automatically
- **Build-time diagnostics** — reports errors and warnings as standard compiler diagnostics

## Comparison

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

## Requirements

- .NET SDK 10.0 or later

## Documentation

For detailed guides, configuration options, and schema type mapping, see the **[documentation site](https://shibayan.github.io/openapi-weaver/)**.

## License

This project is licensed under the [MIT License](https://github.com/shibayan/openapi-weaver/blob/master/LICENSE).
