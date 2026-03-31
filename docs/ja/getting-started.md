# はじめに

## 前提条件

- .NET SDK 8.0 以降
- JSON または YAML 形式の OpenAPI 3.x ドキュメント

## 1. パッケージをインストールする

次のいずれかの方法で、プロジェクトに NuGet パッケージを追加します。

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
> `PrivateAssets="all"` を指定すると、ソースジェネレーターはビルド時にのみ使用され、プロジェクトの推移的依存関係として公開されません。CLI や Package Manager Console でインストールした場合は、生成された `PackageReference` に `PrivateAssets="all"` を手動で追加してください。

## 2. OpenAPI ドキュメントを追加する

### 推奨: `OpenApiWeaverDocument`

任意のメタデータ付きで OpenAPI ドキュメントを含めるには、`OpenApiWeaverDocument` アイテムを使います。

```xml
<ItemGroup>
  <OpenApiWeaverDocument Include="openapi\petstore.yaml"
                         ClientName="PetstoreClient"
                         Namespace="Contoso.Generated" />
</ItemGroup>
```

| Metadata | Required | Default |
|---|---|---|
| `ClientName` | No | ファイル名から導出 (`petstore.yaml` → `PetstoreClient`) |
| `Namespace` | No | プロジェクトの `RootNamespace` |

詳しくは [設定](./configuration) を参照してください。

## 3. 生成されたクライアントを使う

プロジェクトがビルドされると、生成された型はすべて IntelliSense 付きで利用可能になります。

```csharp
// コンストラクター引数は security scheme から生成されます
var client = new PetstoreClient(accessToken: "your-token");

// 必要な場合だけ、独自に構成した HttpClient を渡せます
var customizedClient = new PetstoreClient(new HttpClient(), accessToken: "your-token");

// 操作は OpenAPI tag ごとにグループ化されます
var pet = await client.Pets.GetAsync(petId: 1);
```

生成されたクライアントは、最初の OpenAPI `servers` エントリをもとに `BaseAddress` を設定した内部 `HttpClient` を作成します。すべてのメソッドは async で、任意の `CancellationToken` を受け取れます。

ルートクライアントクラスは `IDisposable` を実装しており、使用後に `Dispose()` を呼び出すことで内部の `HttpClient` を解放できます。また `partial class` として生成されるため、別ファイルで追加のメソッドを定義して拡張できます。

生成パイプラインや内部構造の詳細は [仕組み](./how-it-works) を参照してください。
