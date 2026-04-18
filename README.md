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

You can also pass an existing `HttpClient` to the generated root client. When you do, the generated client reuses that instance, preserves an existing `BaseAddress`, and applies security headers per request instead of mutating `DefaultRequestHeaders`.

The package includes the source generator and all required analyzer assemblies, so no additional dependencies are required.

## Features

- **Incremental source generation** — uses the Roslyn incremental generator pipeline to keep rebuilds efficient
- **OpenAPI 3.0-3.2 support** — reads `.json`, `.yaml`, and `.yml` documents, including OpenAPI 3.2 features such as response summaries and nullable type arrays
- **Tag-based sub-clients** — groups operations by OpenAPI tags and exposes them as properties on the root client
- **Typed request / response models** — generates classes, enums, nested inline types, dictionaries, and composition-aware schema mappings from `components/schemas`, including discriminator-based polymorphic models
- **Multiple request body formats** — supports `application/json`, `application/x-www-form-urlencoded`, and `multipart/form-data`
- **Security scheme support** — supports OAuth2 and bearer tokens, together with API keys in headers, query strings, and cookies
- **Runtime error handling** — throws `OpenApiException`, with typed `OpenApiException<TError>` when error schemas are available
- **OpenAPI-driven XML docs** — generates IntelliSense comments from document, tag, operation, response, and schema metadata, with HTML removed automatically
- **Build-time diagnostics** — reports errors and warnings as standard compiler diagnostics

## Requirements

- .NET SDK 10.0 or later

## Documentation

For detailed guides, configuration options, and schema type mapping, see the **[documentation site](https://shibayan.github.io/openapi-weaver/)**.

## License

This project is licensed under the [MIT License](https://github.com/shibayan/openapi-weaver/blob/master/LICENSE).
