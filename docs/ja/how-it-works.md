# 仕組み

## 生成パイプライン

`OpenApiWeaverDocument` として含まれた各 OpenAPI ドキュメントに対し、ジェネレーターは次の処理を行います。

1. **入力の投影** - 付属の `buildTransitive` ターゲットが `OpenApiWeaverDocument` をコンパイラ可視の追加ファイルへ投影し、`ClientName` や `Namespace` などのメタデータも渡します。
2. **解析** - [Microsoft.OpenApi](https://github.com/microsoft/OpenAPI.NET) を使ってドキュメントを読み込み、OpenAPI 3.0-3.2 の JSON / YAML をサポートします。
3. **変換** - クラス名とメソッド名を導出し、命名規則 (例: `snake_case` → `PascalCase`) を変換し、`$ref` を解決し、スキーマを分類し、XML ドキュメント用に HTML を除去します。
4. **グループ化** - OpenAPI のタグごとに操作をサブクライアントへ整理し、タグの説明も生成される XML ドキュメントへ引き継ぎます。
5. **スキーマ生成** - オブジェクトスキーマを sealed class、文字列列挙を `JsonConverter` 付きの `readonly record struct`、整数列挙を標準 `enum` として生成し、プリミティブ型とコレクション型をマッピングします。インラインスキーマはネスト型になります。
6. **クライアント生成** - 各操作について、適切なリクエストボディのシリアライズ、レスポンスのデシリアライズ、OpenAPI メタデータ由来の XML ドキュメント コメントを含む非同期メソッドを生成します。

クライアント生成時には、ランタイムのエラーハンドリング経路も同時に出力されます。非成功レスポンスではステータス、ヘッダー、本文を保持し、一致する型付きエラースキーマが定義されていれば `OpenApiException` または `OpenApiException<TError>` を送出します。

## 生成されるクライアント構造

ルートクライアントクラスは次を担います。

- 内部で `HttpClient` を作成するか、コンストラクター経由で受け取ります。必要に応じて最初の OpenAPI `servers` エントリから `BaseAddress` を設定します。
- Bearer トークンや API キーなどのセキュリティ資格情報をコンストラクター引数として受け取ります。
- タググループごとに 1 つのプロパティを公開します (例: `client.Pets`, `client.Users`)。
- `info`、`tags`、`summary`、`description`、レスポンスメタデータから、クライアント本体と各タグプロパティの XML ドキュメントを生成します。
- `partial class` として生成されるため、別ファイルで拡張できます。
- `IDisposable` を実装し、生成クライアント自身が所有する `HttpClient` のみを解放します。

各タグサブクライアントには、OpenAPI ドキュメントで定義された path、query、header、cookie パラメーターに対応する非同期の操作メソッドが含まれます。配列パラメーターでは、query は同じキーを繰り返し、path、header、cookie はカンマ区切りの値として送信します。

セキュリティスキームは生成クライアント上に保持され、document-level または operation-level の security requirements に従って各 `HttpRequestMessage` に適用されます。これにより query、header、cookie、OAuth2、Bearer token の扱いが統一され、`HttpClient.DefaultRequestHeaders` を変更せずに済みます。

各操作メソッドは `HttpResponseMessage.EnsureSuccessStatusCode()` に依存せず、非成功レスポンスを明示的に処理するため、OpenAPI の型付きエラーペイロードを例外経由で扱えます。

## 命名規則

ジェネレーターは、命名規則を C# の慣習に沿った形へ自動変換します。

- スキーマ名とプロパティ名は `snake_case` から `PascalCase` へ変換
- メソッド名は利用可能な場合 `operationId` から導出し、`Async` サフィックスを付与
- 元の JSON プロパティ名は `[JsonPropertyName]` 属性で保持

## 増分生成

OpenApiWeaver は Roslyn のインクリメンタル ジェネレーター パイプラインを利用し、キャッシュを活用した効率的な再ビルドを実現します。変更されたドキュメントのみを再処理するため、複数の OpenAPI ファイルを含む大規模なソリューションでも効率的です。
