<script setup lang="ts">
import type { RouteLocationRaw } from 'vue-router'

export type AppTabTone = 'active' | 'warning' | 'danger' | 'info' | 'muted'

export interface AppTabItem {
  id: string
  label: string
  to: RouteLocationRaw
  badge?: string
  tone?: AppTabTone
}

const props = defineProps<{ items: readonly AppTabItem[] }>()
</script>

<template>
  <nav class="app-tabs" aria-label="Page sections">
    <RouterLink
      v-for="item in props.items"
      :key="item.id"
      :to="item.to"
      class="app-tabs__item"
      :class="item.tone ? `app-tabs__item--tone-${item.tone}` : ''"
    >
      <span>{{ item.label }}</span>
      <span v-if="item.badge" class="app-tabs__badge">{{ item.badge }}</span>
    </RouterLink>
  </nav>
</template>

<style lang="scss" scoped>
@use '@/common/styles/app-tabs';
</style>
