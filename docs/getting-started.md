# Getting Started

## Prerequisites

- .NET SDK 8.0 or later
- An OpenAPI 3.x document in JSON or YAML format

## 1. Install the package

Add the NuGet package to your project:

```xml
<ItemGroup>
  <PackageReference Include="OpenApiWeaver" Version="x.y.z" PrivateAssets="all" />
</ItemGroup>
```

`PrivateAssets="all"` ensures the source generator is used only at build time and is not exposed as a transitive dependency of your project.

## 2. Add your OpenAPI document

### Recommended: `OpenApiWeaverDocument`

Use the `OpenApiWeaverDocument` item to include your OpenAPI document with optional metadata:

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

See the [Configuration](./configuration) page for full details.

### Alternative: `AdditionalFiles`

For simple scenarios where default naming is sufficient, `AdditionalFiles` works too:

```xml
<ItemGroup>
  <AdditionalFiles Include="openapi\petstore.yaml" />
</ItemGroup>
```

When using `AdditionalFiles`, the client name is always derived from the file name and the namespace defaults to `RootNamespace`.

## 3. Use the generated client

Once the project builds, all generated types are available with full IntelliSense:

```csharp
// Constructor parameters are generated based on security schemes
var client = new PetstoreClient(accessToken: "your-token");

// Operations are grouped by OpenAPI tag
var pet = await client.Pets.GetAsync(petId: 1);
```

The generated client creates an internal `HttpClient` with `BaseAddress` set from the first OpenAPI `servers` entry. All methods are async and accept an optional `CancellationToken`.

## How It Works

For each OpenAPI document included as an `OpenApiWeaverDocument` or `AdditionalFiles` item, the generator performs the following steps:

1. **Parse** — reads the document with [Microsoft.OpenApi](https://github.com/microsoft/OpenAPI.NET), supporting both JSON and YAML formats
2. **Transform** — derives class and method names, converts naming conventions (e.g. `snake_case` → `PascalCase`), resolves `$ref` references, and classifies schemas
3. **Group** — organizes operations by their OpenAPI tags into sub-client classes
4. **Emit schemas** — generates sealed classes for object schemas, `readonly record struct` for enums, and maps primitive / collection types
5. **Emit clients** — generates async methods for each operation, with the correct request body serialization and response deserialization

### Generated client structure

The root client class:

- Creates an internal `HttpClient` with `BaseAddress` set from the first OpenAPI `servers` entry
- Accepts optional security credentials (bearer tokens, API keys) via constructor parameters
- Exposes one property per tag group (e.g. `client.Pets`, `client.Users`)

Each tag sub-client contains the async operation methods, with parameters mapped from path, query, header, and cookie parameters defined in the OpenAPI document.
