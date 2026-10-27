<script setup lang="ts">
import { computed } from 'vue'
import { withBase } from 'vitepress'

// A blog-card style link to an external page. The title, description and
// image are written by hand: the site is static, so nothing is fetched from
// the linked page.
const props = defineProps<{
  url: string
  title: string
  description?: string
  image?: string
}>()

const host = computed(() => new URL(props.url).host)

// Site-relative paths such as /images/foo.png need the site base.
const imageSrc = computed(() =>
  props.image?.startsWith('/') ? withBase(props.image) : props.image
)
</script>

<template>
  <a class="link-card" :href="url" target="_blank" rel="noreferrer">
    <span class="link-card-body">
      <span class="link-card-title">{{ title }}</span>
      <span v-if="description" class="link-card-description">{{ description }}</span>
      <span class="link-card-host">{{ host }}</span>
    </span>
    <span
      v-if="image"
      class="link-card-image"
      :style="{ backgroundImage: `url('${imageSrc}')` }"
    ></span>
  </a>
</template>

<style scoped>
.link-card {
  display: flex;
  margin: 16px 0;
  border: 1px solid var(--vp-c-divider);
  border-radius: 8px;
  overflow: hidden;
  color: inherit;
  text-decoration: none;
  transition: border-color 0.25s;
}

.link-card:hover {
  border-color: var(--vp-c-brand-1);
}

.link-card-body {
  display: flex;
  flex: 1;
  flex-direction: column;
  justify-content: center;
  gap: 4px;
  min-width: 0;
  padding: 16px 20px;
}

.link-card-title {
  font-size: 16px;
  font-weight: 600;
  line-height: 1.5;
  color: var(--vp-c-text-1);
}

/* Up to two lines, then an ellipsis. */
.link-card-description {
  display: -webkit-box;
  overflow: hidden;
  font-size: 13px;
  line-height: 1.6;
  color: var(--vp-c-text-2);
  -webkit-box-orient: vertical;
  -webkit-line-clamp: 2;
  line-clamp: 2;
}

.link-card-host {
  font-size: 13px;
  color: var(--vp-c-text-2);
}

/* A background image, so a tall image cannot make the card taller. */
.link-card-image {
  flex: none;
  width: 30%;
  max-width: 220px;
  background-position: center;
  background-size: cover;
}

/* Narrow screens keep the image, a little wider so it stays visible. */
@media (max-width: 480px) {
  .link-card-body {
    padding: 12px 14px;
  }

  .link-card-image {
    width: 35%;
  }
}
</style>
