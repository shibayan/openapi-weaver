---
layout: home

hero:
  name: OpenApiWeaver
  text: OpenAPI から C# クライアントを生成
  tagline: OpenAPI 3.x ドキュメントを、OpenAPI 3.2 を含めてビルド時に型安全な C# HTTP クライアントへ変換する、インクリメンタル Roslyn ソースジェネレーター。
  actions:
    - theme: brand
      text: はじめる
      link: /ja/getting-started
    - theme: alt
      text: GitHub で見る
      link: https://github.com/shibayan/openapi-weaver

features:
  - title: 実行時オーバーヘッドなし
    details: すべてのコードは Roslyn ソースジェネレーターによりコンパイル時に生成されるため、実行時コード生成、リフレクション、追加のランタイム依存関係は不要です。
  - title: 増分生成で高速
    details: Roslyn の incremental generator パイプラインを活用し、変更されたドキュメントだけを再処理するため、大規模なソリューションでも再ビルドを高速に保てます。
  - title: NuGet パッケージを追加するだけ
    details: パッケージにはソースジェネレーター本体と必要な analyzer アセンブリが同梱されています。CLI ツールや追加の MSBuild ステップは不要です。
  - title: 強い型付けと IntelliSense
    details: 生成されたクライアント、操作、DTO のすべてで完全な IntelliSense、コンパイル時型安全性、移動サポートを利用できます。
  - title: JSON と YAML をサポート
    details: OpenAPI 3.0-3.2 ドキュメントを JSON (.json) と YAML (.yaml, .yml) 形式で読み取れます。
  - title: ビルド時診断
    details: 無効または未対応の OpenAPI ドキュメントに対して、標準のコンパイラ診断としてエラーや警告を報告します。
---
