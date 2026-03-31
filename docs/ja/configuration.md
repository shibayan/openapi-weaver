# 設定

## `OpenApiWeaverDocument` アイテム

OpenApiWeaver を設定する推奨方法は、`OpenApiWeaverDocument` MSBuild アイテムを使うことです。次のメタデータをサポートします。

| Metadata | Required | Description | Default |
|---|---|---|---|
| `Include` | Yes | OpenAPI ドキュメントへのパス (`.json`, `.yaml`, `.yml`) | - |
| `ClientName` | No | 生成されるルートクライアントクラス名 | ファイル名 → PascalCase + `Client` |
| `Namespace` | No | 生成されるすべての型の名前空間 | プロジェクトの `RootNamespace` |

### 例

```xml
<ItemGroup>
  <OpenApiWeaverDocument Include="openapi\petstore.yaml"
                         ClientName="PetstoreClient"
                         Namespace="Contoso.Generated" />
</ItemGroup>
```

### 複数ドキュメント

1 つのプロジェクトに複数の OpenAPI ドキュメントを含めることができます。各ドキュメントは独立したクライアントを生成します。

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

## `AdditionalFiles` アイテム

単純なシナリオでは `AdditionalFiles` を直接使うこともできます。

```xml
<ItemGroup>
  <AdditionalFiles Include="openapi\petstore.yaml" />
</ItemGroup>
```

`AdditionalFiles` を使う場合、クライアント名は常にファイル名から導出され、名前空間は `RootNamespace` が既定になります。

## クライアント名の導出

`ClientName` を指定しない場合、ジェネレーターはファイル名を PascalCase に変換して `Client` を末尾に付与した名前を導出します。

| File Name | Generated Client Name |
|---|---|
| `petstore.yaml` | `PetstoreClient` |
| `api-schema.json` | `ApiSchemaClient` |
| `my_service.yml` | `MyServiceClient` |

## 対応ドキュメント形式

OpenApiWeaver は次の形式の OpenAPI 3.x ドキュメントを読み込みます。

| Extension | Format |
|---|---|
| `.json` | OpenAPI 3.x JSON |
| `.yaml` | OpenAPI 3.x YAML |
| `.yml` | OpenAPI 3.x YAML |

`AdditionalFiles` または `OpenApiWeaverDocument` に含まれていても、その他の拡張子のファイルはジェネレーターに無視されます。
