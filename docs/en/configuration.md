# Configuration

## `OpenApiWeaverDocument` item

The recommended way to configure OpenApiWeaver is with the `OpenApiWeaverDocument` MSBuild item. It supports the following metadata:

| Metadata | Required | Description | Default |
|---|---|---|---|
| `Include` | Yes | Path to the OpenAPI document (`.json`, `.yaml`, or `.yml`) | - |
| `ClientName` | No | Name of the generated root client class | File name -> PascalCase + `Client` |
| `Namespace` | No | Namespace for all generated types | Project's `RootNamespace` |

### Example

```xml
<ItemGroup>
  <OpenApiWeaverDocument Include="openapi\petstore.yaml"
                         ClientName="PetstoreClient"
                         Namespace="Contoso.Generated" />
</ItemGroup>
```

At build time, the package's `buildTransitive` targets project each `OpenApiWeaverDocument` item into compiler-visible `AdditionalFiles` and forward the `ClientName` and `Namespace` metadata to the source generator.

### Multiple documents

You can include multiple OpenAPI documents in a single project. Each document generates its own independent client:

```xml
<ItemGroup>
  <OpenApiWeaverDocument Include="openapi\petstore.yaml"
                         ClientName="PetstoreClient"
                         Namespace="Contoso.Petstore" />
  <OpenApiWeaverDocument Include="openapi\billing.json"
                         ClientName="BillingClient"
                         Namespace="Contoso.Billing" />
</ItemGroup>
```

## Client name derivation

When `ClientName` is not specified, the generator derives a name from the file name by converting it to PascalCase and appending `Client`:

| File Name | Generated Client Name |
|---|---|
| `petstore.yaml` | `PetstoreClient` |
| `api-schema.json` | `ApiSchemaClient` |
| `my_service.yml` | `MyServiceClient` |

## Supported document formats

OpenApiWeaver reads OpenAPI 3.0-3.2 documents in the following formats:

| Extension | Format |
|---|---|
| `.json` | OpenAPI 3.x JSON |
| `.yaml` | OpenAPI 3.x YAML |
| `.yml` | OpenAPI 3.x YAML |

Other file extensions included via `OpenApiWeaverDocument` are ignored by the generator.

## Document discovery

Only `OpenApiWeaverDocument` items are processed by the generator.

- Files added directly as `AdditionalFiles` are ignored
- `ClientName` and `Namespace` metadata are only available when supplied through `OpenApiWeaverDocument`
