# 設定

## `OpenApiWeaverDocument` アイテム

OpenApiWeaver は `OpenApiWeaverDocument` MSBuild アイテムで設定します。サポートされるメタデータは次のとおりです。

| メタデータ | 必須 | 説明 | 既定値 |
|---|---|---|---|
| `Include` | はい | OpenAPI ドキュメントへのパス (`.json`, `.yaml`, `.yml`) | - |
| `ClientName` | いいえ | 生成されるルートクライアントクラス名 | ファイル名 → PascalCase + `Client` |
| `Namespace` | いいえ | 生成されるすべての型の名前空間 | プロジェクトの `RootNamespace` |

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

1 つのプロジェクトに複数の OpenAPI ドキュメントを含めることができます。各ドキュメントから独立したクライアントが生成されます。

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

`ClientName` を指定しない場合、ジェネレーターはファイル名を PascalCase に変換し、末尾に `Client` を付与した名前を導出します。

| ファイル名 | 生成されるクライアント名 |
|---|---|
| `petstore.yaml` | `PetstoreClient` |
| `api-schema.json` | `ApiSchemaClient` |
| `my_service.yml` | `MyServiceClient` |

## 対応ドキュメント形式

OpenApiWeaver は、次の形式の OpenAPI 3.0-3.2 ドキュメントを読み込みます。

| 拡張子 | 形式 |
|---|---|
| `.json` | OpenAPI 3.x JSON |
| `.yaml` | OpenAPI 3.x YAML |
| `.yml` | OpenAPI 3.x YAML |

これ以外の拡張子を持つファイルは、`OpenApiWeaverDocument` に含まれていてもジェネレーターでは処理されません。

## ドキュメント検出

ジェネレーターが処理するのは `OpenApiWeaverDocument` アイテムだけです。

- `AdditionalFiles` に直接追加したファイルは無視されます
- `ClientName` と `Namespace` のメタデータは `OpenApiWeaverDocument` 経由で指定した場合にのみ利用されます
