# Getting Started

## 1. Install the package

Add the NuGet package to your project. The `PrivateAssets="all"` attribute ensures the source generator is only used at build time and is not included as a transitive dependency.

```xml
<ItemGroup>
  <PackageReference Include="OpenApiWeaver" Version="x.y.z" PrivateAssets="all" />
</ItemGroup>
```

## 2. Add your OpenAPI document

### Recommended: `OpenApiWeaverDocument`

```xml
<ItemGroup>
  <OpenApiWeaverDocument Include="openapi\petstore.yaml"
                         ClientName="PetstoreSdk"
                         Namespace="Contoso.Generated" />
</ItemGroup>
```

`ClientName` and `Namespace` are optional metadata. If omitted, the generator falls back to the file name and the project's `RootNamespace`.

### Alternative: `AdditionalFiles`

`AdditionalFiles` is still supported for simple scenarios:

```xml
<ItemGroup>
  <AdditionalFiles Include="openapi\petstore.yaml" />
</ItemGroup>
```

## 3. Use the generated client

```csharp
var client = new PetstoreClient(accessToken: "your-token");

var pets = await client.Pets.ListAsync();
```

The client name is derived from the file name (e.g. `petstore.yaml` → `PetstoreClient`).

The package bundles the source generator and all required analyzer dependencies — no extra references needed.

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
