# 機能

## 増分ソース生成

OpenApiWeaver は Roslyn の incremental generator パイプラインを活用し、高速でキャッシュ可能な再ビルドを実現します。変更されたドキュメントのみを再処理するため、複数の OpenAPI ファイルを含む大規模なソリューションでも効率的です。

## JSON と YAML のサポート

次の形式の OpenAPI 3.x ドキュメントを読み取れます。

- `.json` - OpenAPI 3.x JSON
- `.yaml` / `.yml` - OpenAPI 3.x YAML

## タグ単位のサブクライアント

操作は OpenAPI の tag ごとにグループ化され、ルートクライアントのプロパティとして公開されます。各 tag は独立したサブクライアントクラスになります。

```csharp
var client = new PetstoreClient(accessToken: "token");

// "Pets" tag -> client.Pets property
var pet = await client.Pets.GetAsync(petId: 1);

// "Users" tag -> client.Users property
var user = await client.Users.GetAsync(userId: "me");
```

メソッド名は、利用可能な場合は `operationId` から導出され、自動的に `Async` サフィックスが付与されます。

## 型付きのリクエスト / レスポンスモデル

オブジェクトスキーマに対して sealed class を生成し、メソッド引数や戻り値へ強く型付けしてマッピングします。

- **Object schemas** -> `[JsonPropertyName]` 属性付きの `sealed class`
- **Enums** -> 専用 `JsonConverter` 付きの `readonly record struct` (文字列 enum)、または標準 `enum` (整数 enum) — [スキーマ型マッピング](./schema-types) を参照
- **Arrays** -> `IReadOnlyList<T>`
- **Dictionaries** (`additionalProperties` / `patternProperties`) -> `IReadOnlyDictionary<string, T>`
- **Inline object / enum schemas** -> 親モデルクラス配下のネスト型

`snake_case` から `PascalCase` へのような命名規則の変換は C# らしい形へ自動変換され、元の JSON 名は `[JsonPropertyName]` で保持されます。

任意 (required でない) かつ nullable なプロパティには `[JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]` が付与され、`null` の場合は JSON ペイロードから省略されます。

## インラインスキーマ / ネスト型

プロパティ定義や配列の `items`、`additionalProperties` 内に出現するインラインのオブジェクト / enum スキーマは、親モデルクラスの **ネスト型** として生成されます。これにより型階層がコンパクトに保たれ、過度に長いトップレベル型名を避けられます。

```csharp
public sealed class Order
{
    [JsonPropertyName("status")]
    public OrderStatusEnum? Status { get; init; }

    // インライン enum スキーマがネスト型として生成される
    public readonly record struct OrderStatusEnum(string Value)
    {
        public static readonly OrderStatusEnum Placed = new("placed");
        public static readonly OrderStatusEnum Approved = new("approved");
    }
}
```

## レスポンス型

生成されるメソッドは、レスポンス内容に応じて異なる型を返します。

| Response Content | Return Type |
|---|---|
| `application/json` | デシリアライズ済みレスポンス型を返す `Task<T>` |
| `text/plain` など | `Task<string>` |
| バイナリコンテンツ | `Task<byte[]>` |
| コンテンツなし (例: 204) | `Task` |

## 複数のリクエストボディ形式

リクエストボディでは次のコンテンツタイプをサポートします。

| Content Type | Serialization |
|---|---|
| `application/json` | `JsonSerializerDefaults.Web` を使う `JsonSerializer` |
| `application/x-www-form-urlencoded` | `FormUrlEncodedContent` |
| `multipart/form-data` | `MultipartFormDataContent` (バイナリファイルアップロード対応) |

### フォーム / マルチパートのコンパイル時専用ポリシー

`application/x-www-form-urlencoded` および `multipart/form-data` のリクエストボディは、すべてのコードがコンパイル時に生成可能でなければなりません。生成できない場合は `OAW004` でエラーになります。

サポートされるフォーム / マルチパートリクエストボディの要件：

- `components/schemas` への参照であること
- 生成時にプロパティが既知なオブジェクト構造であること
- CLR 型に直接マッピングされるプロパティ型 (スカラー、`byte[]`、サポートされるコレクション)

以下はフォーム / マルチパートリクエストボディでは **意図的に非対応** です：

- インラインのリクエストボディスキーマ
- `oneOf` / `anyOf`
- `additionalProperties` / `patternProperties`

## パラメーターの場所

OpenAPI ドキュメントで定義されたパラメーターは、その配置に応じてメソッド引数へマッピングされます。

| Location | Behavior |
|---|---|
| `path` | URL パスへ埋め込まれる |
| `query` | クエリ文字列として追加される |
| `header` | リクエストヘッダーへ追加される |
| `cookie` | `Cookie` ヘッダーへ追加される |

## セキュリティスキームのサポート

OpenAPI ドキュメントで定義された security scheme に応じて、コンストラクター引数が自動生成されます。

| Scheme | Generated Parameter |
|---|---|
| OAuth2 / Bearer token | `string accessToken` - `Authorization: Bearer {token}` として送信 |
| API key (header) | `string apiKey` - カスタムリクエストヘッダーとして送信 |
| API key (query) | `string apiKey` - クエリ文字列へ追加 |
| API key (cookie) | `string apiKey` - `Cookie` ヘッダーとして送信 |

複数の security scheme を組み合わせることもできます。たとえば OAuth2 トークンと API キーの両方が必要な API では、コンストラクターは両方の引数を受け取ります。

## すべての HTTP メソッド

GET、POST、PUT、DELETE、PATCH、HEAD、OPTIONS、TRACE のすべての標準 HTTP メソッドをサポートします。

## ビルド時診断

コンパイル中に、標準のコンパイラ診断としてエラーや警告を報告します。

| Code | Severity | Description |
|---|---|---|
| OAW001 | Error | OpenAPI ドキュメントが空です |
| OAW002 | Warning | OpenAPI ドキュメントに検証警告があります |
| OAW003 | Error | OpenAPI ドキュメントが不正です (例: JSON/YAML の構文不正) |
| OAW004 | Error | OpenAPI ドキュメントで未対応機能が使われています |

これらの診断は、Visual Studio の Error List、`dotnet build` の出力、CI ログに他のコンパイラ診断と同様に表示されます。

## 生成クライアントの拡張性

ルートクライアントクラスは `partial class` として生成されるため、生成コードを変更せずに別ファイルで追加のメソッドやプロパティを定義できます。

ルートクライアントは `IDisposable` も実装しており、`Dispose()` を呼び出すと内部の `HttpClient` を破棄します。

## XML ドキュメントコメント

生成コードには、OpenAPI ドキュメントの `summary`、`description`、パラメーターの説明から導出された `<summary>`、`<remarks>`、`<param>`、`<returns>` の XML ドキュメントコメントが含まれます。これにより、IntelliSense やドキュメントツールで生成 API を完全に把握できます。
