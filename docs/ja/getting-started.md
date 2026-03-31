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

// 操作は OpenAPI tag ごとにグループ化されます
var pet = await client.Pets.GetAsync(petId: 1);
```

生成されたクライアントは、最初の OpenAPI `servers` エントリをもとに `BaseAddress` を設定した内部 `HttpClient` を作成します。すべてのメソッドは async で、任意の `CancellationToken` を受け取れます。

## 仕組み

`OpenApiWeaverDocument` として含まれた各 OpenAPI ドキュメントに対し、ジェネレーターは次の処理を行います。

1. **Parse** - [Microsoft.OpenApi](https://github.com/microsoft/OpenAPI.NET) を使ってドキュメントを読み込み、JSON と YAML の両方をサポート
2. **Transform** - クラス名とメソッド名を導出し、命名規則 (例: `snake_case` → `PascalCase`) を変換し、`$ref` を解決し、スキーマを分類
3. **Group** - OpenAPI tag ごとに操作をサブクライアントへ整理
4. **Emit schemas** - オブジェクトスキーマを sealed class、列挙を `readonly record struct` として生成し、プリミティブ型とコレクション型をマッピング
5. **Emit clients** - 各操作について、適切なリクエストボディのシリアライズとレスポンスのデシリアライズを行う async メソッドを生成

### 生成されるクライアント構造

ルートクライアントクラスは次を担います。

- 最初の OpenAPI `servers` エントリから `BaseAddress` を設定した内部 `HttpClient` を作成する
- 任意のセキュリティ資格情報 (Bearer トークンや API キー) をコンストラクター引数として受け取る
- tag グループごとに 1 つのプロパティを公開する (例: `client.Pets`, `client.Users`)

各 tag サブクライアントには、OpenAPI ドキュメントで定義された path、query、header、cookie パラメーターに対応する async 操作メソッドが含まれます。
