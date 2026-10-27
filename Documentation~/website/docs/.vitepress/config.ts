import { defineConfig } from 'vitepress'

// Published under https://docs.unlibra.com/mururu-blink-lipsync/ from the
// repository's own GitHub Pages deployment on main.
const SITE_URL = 'https://docs.unlibra.com/mururu-blink-lipsync/'

export default defineConfig({
  lang: 'ja',
  title: 'Mururu Blink & LipSync',
  description: 'ムルルに、まばたき機能と声に合わせて口が動くリップシンク機能を追加するUnityツールのマニュアル',
  base: '/mururu-blink-lipsync/',
  cleanUrls: true,
  vite: {
    build: {
      license: { fileName: 'licenses/dependencies.md' }
    }
  },
  // Also feeds <lastmod> in the sitemap; the website workflow fetches the
  // full git history for it.
  lastUpdated: true,

  // Listed from the sitemap index at the root of unlibra.github.io.
  sitemap: {
    hostname: SITE_URL
  },

  transformPageData(pageData, { siteConfig }) {
    // Matches the URLs cleanUrls serves: "foo/index.md" -> "foo/", "foo.md" -> "foo".
    const url = SITE_URL + pageData.relativePath.replace(/(^|\/)index\.md$/, '$1').replace(/\.md$/, '')
    const head = (pageData.frontmatter.head ??= [])
    head.push(
      ['link', { rel: 'canonical', href: url }],
      ['meta', { property: 'og:url', content: url }],
      ['meta', { property: 'og:title', content: pageData.title }],
      ['meta', { property: 'og:description', content: pageData.frontmatter.description ?? siteConfig.site.description }]
    )
  },

  head: [
    ['meta', { property: 'og:type', content: 'website' }],
    ['meta', { property: 'og:site_name', content: 'Mururu Blink & LipSync' }],
    ['meta', { property: 'og:locale', content: 'ja_JP' }],
    // The same OGP image as unlibra.com, for every site on docs.unlibra.com.
    ['meta', { property: 'og:image', content: 'https://unlibra.com/ogp.png' }],
    ['meta', { property: 'og:image:width', content: '1200' }],
    ['meta', { property: 'og:image:height', content: '630' }],
    ['meta', { name: 'twitter:card', content: 'summary_large_image' }],
    // The shared unlibra icons, served from the root of docs.unlibra.com.
    ['link', { rel: 'icon', href: 'https://docs.unlibra.com/favicon.ico', sizes: '32x32' }],
    ['link', { rel: 'icon', type: 'image/svg+xml', href: 'https://docs.unlibra.com/favicon.svg' }],
    ['link', { rel: 'apple-touch-icon', href: 'https://docs.unlibra.com/apple-touch-icon.png' }],
  ],

  themeConfig: {
    // A full URL: a site path would get this site's base prepended.
    // alt: the brand, read before the site title in the link.
    logo: { src: 'https://docs.unlibra.com/brand/symbol.svg', alt: 'unlibra' },

    nav: [
      { text: 'マニュアル', link: '/installation' },
      { text: 'BOOTH', link: 'https://unlibra.booth.pm/items/9056145' }
    ],

    sidebar: [
      {
        text: 'マニュアル',
        items: [
          { text: 'インストール', link: '/installation' },
          { text: '表情ごとの設定', link: '/settings' },
          { text: '更新・修復・アンインストール', link: '/maintenance' },
          { text: 'FAQ', link: '/faq' }
        ]
      },
      {
        text: 'その他',
        items: [
          { text: '対応環境', link: '/requirements' },
          { text: 'ライセンス', link: '/license' },
          { text: '第三者ライセンス', link: '/third-party' },
          { text: 'お問い合わせ', link: '/contact' }
        ]
      }
    ],

    outline: { label: 'このページの内容' },
    docFooter: { prev: '前のページ', next: '次のページ' },
    lastUpdated: { text: '最終更新日' },
    darkModeSwitchLabel: '外観',
    lightModeSwitchTitle: 'ライトモードに切り替え',
    darkModeSwitchTitle: 'ダークモードに切り替え',
    sidebarMenuLabel: 'メニュー',
    returnToTopLabel: 'ページの先頭へ',
    langMenuLabel: '言語を変更',
    navMenuLabel: 'メインナビゲーション',
    mobileMenuLabel: 'メニュー',
    extraMenuLabel: 'その他のオプション',
    skipToContentLabel: '本文へ移動',
    notFound: {
      title: 'ページが見つかりません',
      quote: 'URLが間違っているか、ページが移動した可能性があります。',
      linkLabel: 'トップページへ',
      linkText: 'トップページへ'
    }
  }
})
