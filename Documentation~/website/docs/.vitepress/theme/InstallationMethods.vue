<script setup lang="ts">
import { onMounted, onUnmounted, ref } from 'vue'

const methods = [
  { id: 'vcc', label: 'VCC版', anchor: 'vccから追加する' },
  { id: 'unitypackage', label: 'unitypackage版', anchor: 'unitypackageから追加する' }
] as const
const active = ref<'vcc' | 'unitypackage'>('vcc')

// Keep links to the previous section headings working for either method.
function selectFromHash() {
  const method = methods.find(method => window.location.hash === '#' + method.anchor ||
    window.location.hash === '#' + encodeURIComponent(method.anchor))
  if (method) active.value = method.id
}

function onKey(event: KeyboardEvent) {
  if (!['ArrowLeft', 'ArrowRight', 'Home', 'End'].includes(event.key)) return
  event.preventDefault()
  active.value = event.key === 'Home' ? 'vcc' : event.key === 'End' ? 'unitypackage' :
    active.value === 'vcc' ? 'unitypackage' : 'vcc'
  const button = event.currentTarget as HTMLButtonElement
  button.parentElement?.querySelector<HTMLButtonElement>('[data-method="' + active.value + '"]')?.focus()
}

onMounted(() => {
  selectFromHash()
  window.addEventListener('hashchange', selectFromHash)
})
onUnmounted(() => window.removeEventListener('hashchange', selectFromHash))
</script>

<template>
  <div class="installation-methods">
    <div class="method-tabs" role="tablist" aria-label="インストール方法">
      <button
        v-for="method in methods"
        :id="'install-tab-' + method.id"
        :key="method.id"
        type="button"
        role="tab"
        :data-method="method.id"
        :aria-controls="method.anchor"
        :aria-selected="active === method.id"
        :tabindex="active === method.id ? 0 : -1"
        @click="active = method.id"
        @keydown="onKey"
      >{{ method.label }}</button>
    </div>
    <div
      v-for="method in methods"
      :id="method.anchor"
      :key="method.id"
      class="method-panel"
      role="tabpanel"
      :aria-labelledby="'install-tab-' + method.id"
      :hidden="active !== method.id"
      tabindex="0"
    >
      <slot :name="method.id" />
    </div>
  </div>
</template>

<style scoped>
.installation-methods {
  margin: 24px 0;
  border: 1px solid var(--vp-c-divider);
  border-radius: 8px;
  overflow: hidden;
}
.method-tabs {
  display: flex;
  border-bottom: 1px solid var(--vp-c-divider);
  background: var(--vp-c-bg-soft);
}
.method-tabs button {
  flex: 1;
  min-width: 0;
  padding: 12px 8px;
  border-bottom: 3px solid transparent;
  font: inherit;
  font-weight: 600;
  color: var(--vp-c-text-2);
  cursor: pointer;
}
.method-tabs button[aria-selected="true"] {
  border-bottom-color: var(--vp-c-brand-1);
  background: var(--vp-c-bg);
  color: var(--vp-c-brand-1);
}
.method-tabs button:focus-visible,
.method-panel:focus-visible {
  outline: 2px solid var(--vp-c-brand-1);
  outline-offset: -3px;
}
.method-panel { padding: 4px 20px; }
.method-panel :deep(.vcc-add-button) {
  display: inline-block;
  padding: 8px 20px;
  border-radius: 6px;
  background: var(--vp-c-brand-3);
  color: var(--vp-c-white);
  font-weight: 600;
  text-decoration: none;
}
.method-panel :deep(.vcc-add-button:hover) {
  background: var(--vp-c-brand-2);
}
.method-panel :deep(.vcc-add-button:focus-visible) {
  outline: 2px solid var(--vp-c-brand-1);
  outline-offset: 3px;
}
</style>
