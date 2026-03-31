# Configuration

## `OpenApiWeaverDocument` item

The recommended way to configure OpenApiWeaver is with the `OpenApiWeaverDocument` MSBuild item. It supports the following metadata:

| Metadata | Required | Description |
|---|---|---|
| `Include` | Yes | Path to the OpenAPI document (`.json`, `.yaml`, or `.yml`) |
| `ClientName` | No | Name of the generated root client class. Defaults to the file name (e.g. `petstore.yaml` → `PetstoreClient`) |
| `Namespace` | No | Namespace for all generated types. Defaults to the project's `RootNamespace` |

### Example

```xml
<ItemGroup>
  <OpenApiWeaverDocument Include="openapi\petstore.yaml"
                         ClientName="PetstoreSdk"
                         Namespace="Contoso.Generated" />
</ItemGroup>
```

## `AdditionalFiles` item

For simple scenarios you can use `AdditionalFiles` directly:

```xml
<ItemGroup>
  <AdditionalFiles Include="openapi\petstore.yaml" />
</ItemGroup>
```

When using `AdditionalFiles` the client name is always derived from the file name and the namespace defaults to the project's `RootNamespace`.

## Supported document formats

OpenApiWeaver reads OpenAPI documents in the following formats:

- `.json` — OpenAPI 3.x JSON
- `.yaml` / `.yml` — OpenAPI 3.x YAML
