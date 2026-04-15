# Getting Started

## Prerequisites

- .NET SDK 10.0 or later
- An OpenAPI 3.0-3.2 document in JSON or YAML format

## 1. Install the package

Add the NuGet package to your project by using one of the following methods:

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

Declare the OpenAPI document with the `OpenApiWeaverDocument` item and, if required, specify metadata such as the generated client name and namespace:

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

Files added only as `AdditionalFiles` are ignored. Use `OpenApiWeaverDocument` so the bundled MSBuild targets can project the document and its metadata into the source generator.

See the [Configuration](./configuration) page for full details.

## 3. Use the generated client

After the project builds, the generated types become available with full IntelliSense support:

```csharp
// Constructor parameters are generated based on security schemes
var client = new PetstoreClient(accessToken: "your-token");

// Or reuse an existing HttpClient instance
using var httpClient = new HttpClient
{
    BaseAddress = new Uri("https://api.example.com/")
};
var injectedClient = new PetstoreClient(httpClient, accessToken: "your-token");

// Operations are grouped by OpenAPI tag
var pet = await client.Pets.GetAsync(petId: 1);
```

The generated client can either create its own `HttpClient` or accept one through constructor injection. When the generated client creates the instance, it sets `BaseAddress` from the first OpenAPI `servers` entry. When an existing `HttpClient` is injected, its current `BaseAddress` is preserved unless it is `null`. All generated methods are asynchronous and accept an optional `CancellationToken`.

Security credentials are stored on the generated client and applied to each `HttpRequestMessage`, so injected `HttpClient` instances do not need authentication state in `DefaultRequestHeaders`.

The root client class implements `IDisposable`. If it created the underlying `HttpClient`, `Dispose()` releases it. Injected `HttpClient` instances are not disposed by the generated client. The root client is also generated as a `partial class`, so you can extend it in a separate file.

For details on the generation pipeline and internal structure, see [How It Works](./how-it-works).
