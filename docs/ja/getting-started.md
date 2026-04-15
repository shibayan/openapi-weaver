# はじめに

## 前提条件

- .NET SDK 10.0 以降
- JSON または YAML 形式の OpenAPI 3.0-3.2 ドキュメント

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

OpenAPI ドキュメントは `OpenApiWeaverDocument` アイテムとして宣言し、必要に応じて生成されるクライアント名や名前空間を指定します。

```xml
<ItemGroup>
  <OpenApiWeaverDocument Include="openapi\petstore.yaml"
                         ClientName="PetstoreClient"
                         Namespace="Contoso.Generated" />
</ItemGroup>
```

| メタデータ | 必須 | 既定値 |
|---|---|---|
| `ClientName` | いいえ | ファイル名から導出 (`petstore.yaml` → `PetstoreClient`) |
| `Namespace` | いいえ | プロジェクトの `RootNamespace` |

`AdditionalFiles` に直接追加しただけのファイルは処理されません。付属の MSBuild ターゲットがドキュメントとメタデータをソースジェネレーターへ受け渡せるよう、`OpenApiWeaverDocument` を使用してください。

詳しくは [設定](./configuration) を参照してください。

## 3. 生成されたクライアントを使う

プロジェクトのビルド後は、生成された型を IntelliSense 付きで利用できます。

```csharp
// コンストラクター引数はセキュリティスキームから生成されます
var client = new PetstoreClient(accessToken: "your-token");

// 既存の HttpClient を再利用することもできます
using var httpClient = new HttpClient
{
    BaseAddress = new Uri("https://api.example.com/")
};
var injectedClient = new PetstoreClient(httpClient, accessToken: "your-token");

// 操作は OpenAPI のタグごとにグループ化されます
var pet = await client.Pets.GetAsync(petId: 1);
```

生成されたクライアントは、自身で `HttpClient` を作成することも、コンストラクター経由で受け取ることもできます。生成側で作成した場合は最初の OpenAPI `servers` エントリをもとに `BaseAddress` を設定します。既存の `HttpClient` を注入した場合は、`BaseAddress` が `null` でない限りその値を維持します。生成されるメソッドはすべて非同期で、任意の `CancellationToken` を受け取れます。

セキュリティ資格情報は生成クライアント上に保持され、各 `HttpRequestMessage` に対して適用されます。そのため、注入した `HttpClient` の `DefaultRequestHeaders` に認証状態を持たせる必要はありません。

ルートクライアントクラスは `IDisposable` を実装しており、生成クライアント自身が作成した `HttpClient` のみ `Dispose()` で解放します。外部から注入した `HttpClient` は生成クライアントでは破棄されません。また `partial class` として生成されるため、別ファイルで拡張できます。

生成パイプラインや内部構造の詳細は [仕組み](./how-it-works) を参照してください。
