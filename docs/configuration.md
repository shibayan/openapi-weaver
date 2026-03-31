# Configuration

## `OpenApiWeaverDocument` item

The recommended way to configure OpenApiWeaver is with the `OpenApiWeaverDocument` MSBuild item. It supports the following metadata:

| Metadata | Required | Description | Default |
|---|---|---|---|
| `Include` | Yes | Path to the OpenAPI document (`.json`, `.yaml`, or `.yml`) | — |
| `ClientName` | No | Name of the generated root client class | File name → PascalCase + `Client` |
| `Namespace` | No | Namespace for all generated types | Project's `RootNamespace` |

### Example

```xml
<ItemGroup>
  <OpenApiWeaverDocument Include="openapi\petstore.yaml"
                         ClientName="PetstoreClient"
                         Namespace="Contoso.Generated" />
</ItemGroup>
```

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

## `AdditionalFiles` item

For simple scenarios you can use `AdditionalFiles` directly:

```xml
<ItemGroup>
  <AdditionalFiles Include="openapi\petstore.yaml" />
</ItemGroup>
```

When using `AdditionalFiles`, the client name is always derived from the file name and the namespace defaults to `RootNamespace`.

## Client name derivation

When `ClientName` is not specified, the generator derives a name from the file name by converting it to PascalCase and appending `Client`:

| File Name | Generated Client Name |
|---|---|
| `petstore.yaml` | `PetstoreClient` |
| `api-schema.json` | `ApiSchemaClient` |
| `my_service.yml` | `MyServiceClient` |

## Supported document formats

OpenApiWeaver reads OpenAPI 3.x documents in the following formats:

| Extension | Format |
|---|---|
| `.json` | OpenAPI 3.x JSON |
| `.yaml` | OpenAPI 3.x YAML |
| `.yml` | OpenAPI 3.x YAML |

Other file extensions included via `AdditionalFiles` or `OpenApiWeaverDocument` are ignored by the generator.
