---
layout: home

hero:
  name: OpenApiWeaver
  text: OpenAPI から C# クライアントをビルド時に生成
  tagline: OpenAPI 3.2 を含む OpenAPI 3.x ドキュメントから、型安全な C# HTTP クライアントを生成するインクリメンタル Roslyn ソースジェネレーター。
  image:
    src: /icon.svg
    alt: OpenApiWeaver
  actions:
    - theme: brand
      text: はじめる
      link: /ja/getting-started
    - theme: alt
      text: GitHub で見る
      link: https://github.com/shibayan/openapi-weaver

features:
  - title: 実行時オーバーヘッドなし
    details: すべてのクライアントコードを Roslyn ソースジェネレーターでコンパイル時に生成するため、実行時コード生成、リフレクション、追加のランタイム依存関係は不要です。
  - title: 増分生成で高速
    details: Roslyn のインクリメンタル ジェネレーター パイプラインを利用し、変更されたドキュメントだけを再処理するため、大規模なソリューションでも再ビルドを効率的に保てます。
  - title: NuGet パッケージとして提供
    details: ソースジェネレーター本体と必要なアナライザーアセンブリを単一の NuGet パッケージに含めて提供します。CLI ツールや追加の MSBuild ステップは不要です。
  - title: 強い型付けと IntelliSense
    details: 生成されたクライアント、操作、DTO に対して IntelliSense、コンパイル時の型安全性、コードナビゲーションを提供します。
  - title: JSON と YAML をサポート
    details: OpenAPI 3.0-3.2 ドキュメントを JSON (.json) と YAML (.yaml, .yml) 形式で読み取れます。
  - title: ビルド時診断
    details: 無効または未対応の OpenAPI ドキュメントを、標準のコンパイラ警告およびエラーとして報告します。
---
