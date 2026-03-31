import { defineConfig } from 'vitepress'

export default defineConfig({
  title: 'OpenApiWeaver',
  description: 'An incremental Roslyn source generator that turns OpenAPI documents into strongly typed C# HTTP clients at build time.',

  base: '/openapi-weaver/',

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
          { text: 'Configuration', link: '/configuration' },
          { text: 'Features', link: '/features' },
          { text: 'Schema Type Mapping', link: '/schema-types' }
        ]
      }
    ],

    socialLinks: [
      { icon: 'github', link: 'https://github.com/shibayan/openapi-weaver' }
    ],

    footer: {
      message: 'Released under the MIT License.',
      copyright: 'Copyright © shibayan'
    }
  }
})
