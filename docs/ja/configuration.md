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

ビルド時には、パッケージに含まれる `buildTransitive` ターゲットが各 `OpenApiWeaverDocument` をコンパイラ可視の `AdditionalFiles` へ投影し、`ClientName` と `Namespace` のメタデータもソースジェネレーターへ受け渡します。

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

## クライアント名の導出

`ClientName` を指定しない場合、ジェネレーターはファイル名を PascalCase に変換して `Client` を末尾に付与した名前を導出します。

| File Name | Generated Client Name |
|---|---|
| `petstore.yaml` | `PetstoreClient` |
| `api-schema.json` | `ApiSchemaClient` |
| `my_service.yml` | `MyServiceClient` |

## 対応ドキュメント形式

OpenApiWeaver は次の形式の OpenAPI 3.0-3.2 ドキュメントを読み込みます。

| Extension | Format |
|---|---|
| `.json` | OpenAPI 3.x JSON |
| `.yaml` | OpenAPI 3.x YAML |
| `.yml` | OpenAPI 3.x YAML |

`OpenApiWeaverDocument` に含まれていても、その他の拡張子のファイルはジェネレーターに無視されます。

## ドキュメント検出

ジェネレーターが処理するのは `OpenApiWeaverDocument` アイテムだけです。

- `AdditionalFiles` に直接追加したファイルは無視されます
- `ClientName` と `Namespace` のメタデータは `OpenApiWeaverDocument` 経由で指定した場合にのみ利用されます
