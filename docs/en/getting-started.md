# Getting Started

## Prerequisites

- .NET SDK 10.0 or later
- An OpenAPI 3.0-3.2 document in JSON or YAML format

## 1. Install the package

Add the NuGet package to your project using one of the following methods:

::: code-group

```bash [.NET CLI]
dotnet add package OpenApiWeaver --version x.y.z
```

```powershell [Package Manager Console]
Install-Package OpenApiWeaver -Version x.y.z
```

```xml [PackageReference]
<ItemGroup>
  <PackageReference Include="OpenApiWeaver" Version="x.y.z" PrivateAssets="all" />
</ItemGroup>
```

:::

> [!NOTE]
> `PrivateAssets="all"` ensures the source generator is used only at build time and is not exposed as a transitive dependency of your project. When installing via the CLI or Package Manager Console, add `PrivateAssets="all"` manually to the generated `PackageReference` entry.

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
| `ClientName` | No | Derived from file name (`petstore.yaml` -> `PetstoreClient`) |
| `Namespace` | No | Project's `RootNamespace` |

Files added only as `AdditionalFiles` are ignored. Use `OpenApiWeaverDocument` so the bundled MSBuild targets can project the document and its metadata into the generator.

See the [Configuration](./configuration) page for full details.

## 3. Use the generated client

Once the project builds, all generated types are available with full IntelliSense:

```csharp
// Constructor parameters are generated based on security schemes
var client = new PetstoreClient(accessToken: "your-token");

// Operations are grouped by OpenAPI tag
var pet = await client.Pets.GetAsync(petId: 1);
```

The generated client creates an internal `HttpClient` with `BaseAddress` set from the first OpenAPI `servers` entry. All methods are async and accept an optional `CancellationToken`.

The root client class implements `IDisposable` — call `Dispose()` when you are done using it to release the underlying `HttpClient`. It is also generated as a `partial class`, so you can extend it with additional methods in a separate file.

For details on the generation pipeline and internal structure, see [How It Works](./how-it-works).
