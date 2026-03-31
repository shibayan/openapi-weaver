# 仕組み

## 生成パイプライン

`OpenApiWeaverDocument` として含まれた各 OpenAPI ドキュメントに対し、ジェネレーターは次の処理を行います。

1. **Parse** - [Microsoft.OpenApi](https://github.com/microsoft/OpenAPI.NET) を使ってドキュメントを読み込み、JSON と YAML の両方をサポート
2. **Transform** - クラス名とメソッド名を導出し、命名規則 (例: `snake_case` → `PascalCase`) を変換し、`$ref` を解決し、スキーマを分類
3. **Group** - OpenAPI tag ごとに操作をサブクライアントへ整理
4. **Emit schemas** - オブジェクトスキーマを sealed class、文字列列挙を `JsonConverter` 付きの `readonly record struct`、整数列挙を標準 `enum` として生成し、プリミティブ型とコレクション型をマッピング。インラインスキーマはネスト型になります。
5. **Emit clients** - 各操作について、適切なリクエストボディのシリアライズとレスポンスのデシリアライズを行う async メソッドを生成

## 生成されるクライアント構造

ルートクライアントクラスは次を担います。

- 最初の OpenAPI `servers` エントリから `BaseAddress` を設定した内部 `HttpClient` を作成する
- 任意のセキュリティ資格情報 (Bearer トークンや API キー) をコンストラクター引数として受け取る
- tag グループごとに 1 つのプロパティを公開する (例: `client.Pets`, `client.Users`)
- `partial class` として生成されるため、別ファイルで拡張可能
- `IDisposable` を実装し、内部の `HttpClient` を解放する

各 tag サブクライアントには、OpenAPI ドキュメントで定義された path、query、header、cookie パラメーターに対応する async 操作メソッドが含まれます。

## 命名規則

ジェネレーターは C# の慣習に合うよう命名規則を自動変換します。

- スキーマ名とプロパティ名は `snake_case` から `PascalCase` へ変換
- メソッド名は利用可能な場合 `operationId` から導出し、`Async` サフィックスを付与
- 元の JSON プロパティ名は `[JsonPropertyName]` 属性で保持

## 増分生成

OpenApiWeaver は Roslyn の incremental generator パイプラインを活用し、高速でキャッシュ可能な再ビルドを実現します。変更されたドキュメントのみを再処理するため、複数の OpenAPI ファイルを含む大規模なソリューションでも効率的です。
