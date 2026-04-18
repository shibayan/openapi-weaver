import { defineConfig } from 'vitepress'

export default defineConfig({
  lang: 'en-US',
  title: 'OpenApiWeaver',
  description: 'An incremental Roslyn source generator that turns OpenAPI 3.x documents into strongly typed C# HTTP clients at build time.',

  base: '/openapi-weaver/',

  rewrites: {
    'en/:rest*': ':rest*'
  },

  themeConfig: {
    logo: '/icon.svg',

    search: {
      provider: 'local'
    },

    socialLinks: [
      { icon: 'github', link: 'https://github.com/shibayan/openapi-weaver' }
    ],

    footer: {
      message: 'Released under the MIT License.',
      copyright: 'Copyright © Tatsuro Shibamura'
    }
  },

  locales: {
    root: {
      label: 'English',
      lang: 'en-US',
      themeConfig: {
        nav: [
          { text: 'Guide', link: '/getting-started' },
          { text: 'NuGet', link: 'https://www.nuget.org/packages/OpenApiWeaver' }
        ],

        sidebar: [
          {
            text: 'Guide',
            items: [
              { text: 'Getting Started', link: '/getting-started' },
              { text: 'How It Works', link: '/how-it-works' },
              { text: 'Configuration', link: '/configuration' },
              { text: 'Features', link: '/features' },
              { text: 'Schema Type Mapping', link: '/schema-types' }
            ]
          }
        ]
      }
    },

    ja: {
      label: '日本語',
      lang: 'ja-JP',
      link: '/ja/',
      title: 'OpenApiWeaver',
      description: 'OpenAPI 3.x ドキュメントをビルド時に型安全な C# HTTP クライアントへ変換する、インクリメンタル Roslyn ソースジェネレーター。',
      themeConfig: {
        nav: [
          { text: 'ガイド', link: '/ja/getting-started' },
          { text: 'NuGet', link: 'https://www.nuget.org/packages/OpenApiWeaver' }
        ],

        sidebar: [
          {
            text: 'ガイド',
            items: [
              { text: 'はじめに', link: '/ja/getting-started' },
              { text: '仕組み', link: '/ja/how-it-works' },
              { text: '設定', link: '/ja/configuration' },
              { text: '機能', link: '/ja/features' },
              { text: 'スキーマ型マッピング', link: '/ja/schema-types' }
            ]
          }
        ]
      }
    }
  }
})
