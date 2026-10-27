// The custom fonts replace Inter, so skip bundling and preloading it.
import DefaultTheme from 'vitepress/theme-without-fonts'
import type { Theme } from 'vitepress'
import LinkCard from './LinkCard.vue'
// Served from this site (no third-party font server), in unicode-range
// slices: a page loads only the slices of the characters it shows.
import '@fontsource-variable/noto-sans-jp'
import '@fontsource-variable/geist-mono'
import './custom.css'

export default {
  extends: DefaultTheme,
  enhanceApp({ app }) {
    app.component('LinkCard', LinkCard)
  }
} satisfies Theme
