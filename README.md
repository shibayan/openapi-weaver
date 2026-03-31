# OpenApiWeaver

[![CI](https://github.com/shibayan/openapi-weaver/actions/workflows/ci.yml/badge.svg)](https://github.com/shibayan/openapi-weaver/actions/workflows/ci.yml)
[![Downloads](https://badgen.net/nuget/dt/OpenApiWeaver)](https://www.nuget.org/packages/OpenApiWeaver/)
[![NuGet](https://img.shields.io/nuget/v/OpenApiWeaver)](https://www.nuget.org/packages/OpenApiWeaver)
[![License](https://img.shields.io/github/license/shibayan/openapi-weaver)](LICENSE)

**OpenApiWeaver** is an incremental Roslyn source generator that turns OpenAPI 3.x documents into strongly typed C# HTTP clients at build time. No runtime code generation, no reflection — just plain C# emitted during compilation.

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

**3. Use the generated client**

```csharp
var client = new PetstoreClient(accessToken: "your-token");

// Operations are grouped by OpenAPI tag
var pet = await client.Pets.GetAsync(petId: 1);
```

No extra dependencies are required — the package bundles the source generator and all analyzer assemblies.

## Features

- **Incremental source generation** — fast, cached rebuilds via the Roslyn incremental generator pipeline
- **JSON & YAML support** — reads `.json`, `.yaml`, and `.yml` OpenAPI 3.x documents
- **Tag-based sub-clients** — operations grouped by OpenAPI tags, exposed as properties on the root client
- **Typed request / response models** — sealed classes, enums, and collection types from `components/schemas`
- **Multiple request body formats** — `application/json`, `application/x-www-form-urlencoded`, and `multipart/form-data`
- **Security scheme support** — OAuth2 / Bearer tokens, API keys (header, query, cookie)
- **Build-time diagnostics** — errors and warnings reported as standard compiler diagnostics

## Requirements

- .NET SDK 8.0 or later

## Documentation

For detailed guides, configuration options, and schema type mapping, visit the **[documentation site](https://shibayan.github.io/openapi-weaver/)**.

## License

This project is licensed under the [MIT License](LICENSE).
