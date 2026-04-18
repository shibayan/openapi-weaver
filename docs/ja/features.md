# 機能

## 増分ソース生成

OpenApiWeaver は Roslyn のインクリメンタル ジェネレーター パイプラインを利用し、キャッシュを活用した効率的な再ビルドを実現します。変更されたドキュメントのみを再処理するため、複数の OpenAPI ファイルを含む大規模なソリューションでも効率的です。

## JSON と YAML のサポート

OpenApiWeaver は次の形式の OpenAPI 3.0-3.2 ドキュメントを読み取ります。

- `.json` - OpenAPI 3.x JSON
- `.yaml` / `.yml` - OpenAPI 3.x YAML

## タグ単位のサブクライアント

操作は OpenAPI のタグごとにグループ化され、ルートクライアントのプロパティとして公開されます。各タグは独立したサブクライアントクラスになります。

```csharp
var client = new PetstoreClient(accessToken: "token");

// "Pets" タグ -> client.Pets プロパティ
var pet = await client.Pets.GetAsync(petId: 1);

// "Users" タグ -> client.Users プロパティ
var user = await client.Users.GetAsync(userId: "me");
```

メソッド名は、利用可能な場合は `operationId` から導出され、自動的に `Async` サフィックスが付与されます。

タグに説明がある場合、その内容は生成されるタグプロパティとサブクライアントクラスの XML ドキュメント コメントとして出力されます。

## 型付きのリクエスト / レスポンスモデル

OpenApiWeaver はオブジェクトスキーマに対してクラスを生成し、メソッド引数や戻り値へ型付きでマッピングします。

- **オブジェクトスキーマ** -> `[JsonPropertyName]` 属性付きの `sealed class`
- **ポリモーフィックスキーマ** -> スキーマが `discriminator` と `oneOf` を使う場合はベースクラスと派生クラスを生成
- **列挙型** -> 文字列 enum は専用 `JsonConverter` 付きの `readonly record struct`、整数 enum は標準 `enum`。詳細は [スキーマ型マッピング](./schema-types) を参照
- **配列** -> `IReadOnlyList<T>`
- **辞書** (`additionalProperties` / `patternProperties`) -> `IReadOnlyDictionary<string, T>`
- **インラインのオブジェクト / enum スキーマ** -> 親モデルクラス配下のネスト型

`snake_case` から `PascalCase` へのような命名規則の変換は C# の慣習に沿って自動的に行われ、元の JSON 名は `[JsonPropertyName]` で保持されます。

任意かつ nullable なプロパティには `[JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]` が付与され、`null` の場合は JSON ペイロードから省略されます。

OpenAPI 3.2 の nullable type array である `type: ["string", "null"]` のような表現は、`string?` のような nullable CLR 型へマッピングされます。

コンポーネントスキーマが `discriminator` と、名前付きスキーマを参照する `oneOf` を併用している場合、OpenApiWeaver は `[JsonPolymorphic]` と `[JsonDerivedType]` を使ったポリモーフィックなベースモデルと、その派生型を生成します。

## インラインスキーマ / ネスト型

プロパティ定義や配列の `items`、`additionalProperties` 内に出現するインラインのオブジェクト / enum スキーマは、親モデルクラスのネスト型として生成されます。これにより型階層がコンパクトに保たれ、過度に長いトップレベル型名を避けられます。

```csharp
public sealed class Order
{
    [JsonPropertyName("status")]
    public Order.StatusEnum? Status { get; init; }

    // インライン enum スキーマがネスト型として生成される
    public readonly record struct StatusEnum(string Value)
    {
        public static readonly StatusEnum Placed = new("placed");
        public static readonly StatusEnum Approved = new("approved");
    }
}
```

## discriminator を使ったポリモーフィックスキーマ

OpenApiWeaver は、次の条件を満たすコンポーネントスキーマに対して OpenAPI の discriminator ベースのポリモーフィズムをサポートします。

- ベーススキーマが `discriminator.propertyName` を持つ
- ベーススキーマが `oneOf` を使う
- `oneOf` の各要素が `components/schemas` 内の名前付きスキーマへの `$ref` である

生成されるベース型は、System.Text.Json のポリモーフィックシリアライズ用属性が付与された non-sealed なクラスになります。参照された各スキーマは派生型として出力されます。`discriminator.mapping` がある場合はそのキーが discriminator 値として使われ、ない場合は参照先のスキーマ名が使われます。

```yaml
animal:
    type: object
    discriminator:
        propertyName: kind
        mapping:
            dog: '#/components/schemas/dog'
            cat: '#/components/schemas/cat'
    oneOf:
        - $ref: '#/components/schemas/dog'
        - $ref: '#/components/schemas/cat'
dog:
    allOf:
        - $ref: '#/components/schemas/animal'
        - type: object
            properties:
                barks:
                    type: boolean
cat:
    allOf:
        - $ref: '#/components/schemas/animal'
        - type: object
            properties:
                lives:
                    type: integer
```

```csharp
[JsonPolymorphic(TypeDiscriminatorPropertyName = "kind")]
[JsonDerivedType(typeof(Dog), typeDiscriminator: "dog")]
[JsonDerivedType(typeof(Cat), typeDiscriminator: "cat")]
public class Animal
{
    [JsonPropertyName("name")]
    public required string Name { get; init; }
}

public sealed class Dog : Animal
{
    [JsonPropertyName("barks")]
    public required bool Barks { get; init; }
}
```

discriminator 用のプロパティ自体は通常の CLR プロパティとしては生成されません。これにより、ポリモーフィックシリアライズ時の重複した JSON 出力を避けます。

未対応の discriminator パターンは `OAW004` で生成失敗になります。対象には、`anyOf` と組み合わせた discriminator、`oneOf` を持たない discriminator、インラインの `oneOf` メンバー、重複した discriminator 値、`oneOf` に含まれないスキーマを参照する mapping が含まれます。

## レスポンス型

生成されるメソッドは、レスポンス内容に応じて次の型を返します。

| レスポンス内容 | 戻り値型 |
|---|---|
| `application/json` | デシリアライズ済みレスポンス型を返す `Task<T>` |
| `text/plain` など | `Task<string>` |
| バイナリコンテンツ | `Task<byte[]>` |
| コンテンツなし (例: 204) | `Task` |

成功レスポンスが複数ある場合は、より低いステータスの本文なしレスポンスよりも、本文を持つレスポンスが優先されます。レスポンスのメディアタイプが複数ある場合は、JSON、バイナリ、テキストの順に優先されます。

## ランタイムのエラーハンドリング

生成されるクライアントは `EnsureSuccessStatusCode()` を呼びません。代わりに、非成功レスポンス用の明示的なエラーハンドリングコードを生成し、OpenAPI に定義されたエラー情報を呼び出し元で扱えるようにします。

- 非 2xx レスポンスでは `OpenApiException` を送出します
- 例外には `StatusCode`、`ReasonPhrase`、`ContentType`、生の `ResponseContent` が含まれます
- OpenAPI 操作に一致するエラーレスポンススキーマがある場合、デシリアライズ済みの `Error` を持つ `OpenApiException<TError>` を送出します
- マッチング順は、厳密なステータスコード、`4XX` のようなワイルドカード、`default` の順です

JSON のエラーレスポンスでは、`Content-Type` が JSON 互換のときだけデシリアライズを試みます。デシリアライズに失敗した場合は、非ジェネリックの `OpenApiException` へフォールバックし、生のレスポンス本文を保持します。

成功レスポンスで JSON 本文が必須のケースでは、本文が空の場合に `InvalidOperationException("The response body was empty.")` が送出されます。

使用例:

```csharp
try
{
    await client.Partners.CreatePartnerAsync(body, cancellationToken);
}
catch (OpenApiException<ValidationProblem> exception) when (exception.StatusCode == 400)
{
    Console.WriteLine($"Validation failed: {exception.Error?.Message}");
}
catch (OpenApiException exception)
{
    Console.WriteLine($"Request failed: {exception.StatusCode} {exception.ReasonPhrase}");
    Console.WriteLine($"Content-Type: {exception.ContentType}");
    Console.WriteLine(exception.ResponseContent);
}
```

## 複数のリクエストボディ形式

OpenApiWeaver はリクエストボディで次のコンテンツタイプをサポートします。

| Content Type | シリアライズ方法 |
|---|---|
| `application/json` | `JsonSerializerDefaults.Web` を使う `JsonSerializer` |
| `application/x-www-form-urlencoded` | `FormUrlEncodedContent` |
| `multipart/form-data` | `MultipartFormDataContent` (バイナリファイルアップロード対応) |

1 つのリクエストボディに複数のサポート済みメディアタイプが定義されている場合は、利用可能であれば JSON が優先されます。

### フォーム / マルチパートのコンパイル時専用ポリシー

`application/x-www-form-urlencoded` および `multipart/form-data` のリクエストボディは、必要なコードをすべてコンパイル時に生成できなければなりません。生成できない場合は `OAW004` でエラーになります。

サポートされるフォーム / マルチパートリクエストボディの要件：

- `components/schemas` への参照であること
- 生成時にプロパティが既知なオブジェクト構造であること
- CLR 型に直接マッピングされるプロパティ型 (スカラー、`byte[]`、サポートされるコレクション)

以下の機能は、フォームおよびマルチパートのリクエストボディでは意図的にサポートしていません。

- インラインのリクエストボディスキーマ
- `oneOf` / `anyOf`
- `additionalProperties` / `patternProperties`

## パラメーターの場所

OpenAPI ドキュメントで定義されたパラメーターは、その場所に応じてメソッド引数へマッピングされます。

| 場所 | 挙動 |
|---|---|
| `path` | URL パスへ埋め込まれる |
| `query` | クエリ文字列として追加される |
| `header` | リクエストヘッダーへ追加される |
| `cookie` | `Cookie` ヘッダーへ追加される |

## セキュリティスキームのサポート

OpenAPI ドキュメントで定義されたセキュリティスキームに応じて、コンストラクター引数が自動生成されます。

| スキーム | 生成される引数 |
|---|---|
| OAuth2 / Bearer token | `string accessToken` - `Authorization: Bearer {token}` として送信 |
| API key (header) | `string apiKey` - カスタムリクエストヘッダーとして送信 |
| API key (query) | `string apiKey` - クエリ文字列へ追加 |
| API key (cookie) | `string apiKey` - `Cookie` ヘッダーとして送信 |

複数のセキュリティスキームを組み合わせることもできます。たとえば OAuth2 トークンと API キーの両方が必要な API では、コンストラクターは両方の引数を受け取ります。

これらの値は生成される各 `HttpRequestMessage` に対して適用されます。header、query、cookie、OAuth2、Bearer token を一貫した方針で扱うため、外部から注入した `HttpClient` の `DefaultRequestHeaders` に認証状態を持たせる必要がありません。

## すべての HTTP メソッド

OpenApiWeaver は GET、POST、PUT、DELETE、PATCH、HEAD、OPTIONS、TRACE のすべての標準 HTTP メソッドをサポートします。

## ビルド時診断

OpenApiWeaver は、コンパイル中に標準のコンパイラ診断としてエラーや警告を報告します。

| コード | 重要度 | 説明 |
|---|---|---|
| OAW001 | Error | OpenAPI ドキュメントが空です |
| OAW002 | Warning | OpenAPI ドキュメントに検証警告があります |
| OAW003 | Error | OpenAPI ドキュメントが不正です (例: JSON/YAML の構文不正) |
| OAW004 | Error | OpenAPI ドキュメントで未対応機能が使われています |

これらの診断は、Visual Studio の Error List、`dotnet build` の出力、CI ログに他のコンパイラ診断と同様に表示されます。

## 生成クライアントの拡張性

ルートクライアントクラスは `partial class` として生成されるため、生成コードを変更せずに別ファイルで拡張できます。

ルートクライアントは `IDisposable` も実装しており、内部で作成した `HttpClient` のみ `Dispose()` で破棄します。外部から注入した `HttpClient` は生成クライアントでは破棄しません。

## XML ドキュメントコメント

生成コードには、`info`、タグの説明、操作の `summary` と `description`、パラメーターの説明、レスポンスの `summary` と `description`、スキーマの `title` と `description` から導出された `<summary>`、`<remarks>`、`<param>`、`<returns>` の XML ドキュメント コメントが含まれます。HTML マークアップは自動的に除去されるため、生成される IntelliSense を読みやすく保てます。
