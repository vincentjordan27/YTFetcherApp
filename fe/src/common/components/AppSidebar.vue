<script setup lang="ts">
import { computed } from 'vue'
import { useRoute, useRouter } from 'vue-router'
import { useAuthentication } from '@/app/auth/composables/useAuthentication'
import logoUrl from '@/common/assets/yt-fetcher-logo.png'
import { useAppShellCounts } from '@/common/composables/useAppShellCounts'
import { designAppShell } from '@/common/data/app-shell.design-data'
import type { AppTabItem } from '@/common/components/AppTabs.vue'

const route = useRoute()
const router = useRouter()
const { badgeFor } = useAppShellCounts()
const { authenticationEnabled, identity, logout } = useAuthentication()

/** Labels and routes stay static; the two live badges come from the shell counts composable. */
const navigation = computed<readonly AppTabItem[]>(() =>
  designAppShell.navigation.map(item => ({ ...item, badge: badgeFor(item.id) })),
)

/**
 * Anonymous deployments must clearly identify the session as a guest. Authenticated deployments show the
 * real session identity, so neither mode presents a fabricated operator.
 */
const operator = computed(() => {
  if (!authenticationEnabled.value) {
    return {
      initials: 'G',
      name: 'Guest',
      email: 'Authentication disabled',
      role: 'Guest',
    }
  }

  const current = identity.value

  if (current === null) {
    return null
  }

  return {
    initials: initialsFor(current.displayName, current.email),
    name: current.displayName ?? current.email,
    email: current.email,
    role: current.role,
  }
})

function isNavigationItemActive(itemId: string): boolean {
  return route.path.startsWith(`/${itemId}`)
}

function initialsFor(displayName: string | null, email: string): string {
  const source = displayName ?? email.split('@')[0] ?? email
  const words = source.split(/[\s._-]+/).filter(word => word.length > 0)
  const initials = words.slice(0, 2).map(word => word.charAt(0)).join('')

  return initials.length > 0 ? initials.toUpperCase() : email.charAt(0).toUpperCase()
}

async function handleLogout(): Promise<void> {
  await logout()
  await router.push('/login')
}
</script>

<template>
  <aside class="app-sidebar">
    <div class="app-sidebar__top">
      <RouterLink to="/videos/active" class="app-sidebar__brand" title="YT Fetcher">
        <span class="app-sidebar__logo-box"><img :src="logoUrl" alt="YT Fetcher" class="app-sidebar__logo" width="36" height="36" decoding="async" /></span>
        <span class="app-sidebar__version">{{ designAppShell.version }}</span>
      </RouterLink>
      <div class="app-sidebar__divider" />
      <nav class="app-sidebar__navigation" aria-label="Primary navigation">
        <RouterLink
          v-for="item in navigation"
          :key="item.id"
          :to="item.to"
          class="app-sidebar__link"
          :class="{ 'app-sidebar__link--active': isNavigationItemActive(item.id) }"
        >
          <span class="app-sidebar__indicator" />
          <span class="app-sidebar__icon-wrap">
            <svg v-if="item.id === 'videos'" class="app-sidebar__icon" viewBox="0 0 24 24" fill="currentColor" aria-hidden="true"><path d="M20 4H4c-1.1 0-2 .9-2 2v12c0 1.1.9 2 2 2h16c1.1 0 2-.9 2-2V6c0-1.1-.9-2-2-2zm-10 10.5v-5l5 2.5-5 2.5z" /></svg>
            <svg v-else-if="item.id === 'channels'" class="app-sidebar__icon" viewBox="0 0 24 24" fill="currentColor" aria-hidden="true"><path d="M20 8H4V6h16v2zm-2-6H6v2h12V2zm4 10v8c0 1.1-.9 2-2 2H4c-1.1 0-2-.9-2-2v-8c0-1.1.9-2 2-2h16c1.1 0 2 .9 2 2zm-6 4l-6-3.27v6.53L16 16z" /></svg>
            <svg v-else-if="item.id === 'usage'" class="app-sidebar__icon" viewBox="0 0 24 24" fill="currentColor" aria-hidden="true"><path d="M5 9.2h3V19H5V9.2zM10.6 5h2.8v14h-2.8V5zm5.6 8H19v6h-2.8v-6z" /></svg>
            <svg v-else-if="item.id === 'backfill-runs'" class="app-sidebar__icon" viewBox="0 0 24 24" fill="currentColor" aria-hidden="true"><path d="M13 3a9 9 0 00-9 9H1l3.89 3.89.07.14L9 12H6c0-3.87 3.13-7 7-7s7 3.13 7 7-3.13 7-7 7c-1.93 0-3.68-.79-4.94-2.06l-1.42 1.42A8.954 8.954 0 0013 21a9 9 0 000-18zm-1 5v5l4.28 2.54.72-1.21-3.5-2.08V8h-1.5z" /></svg>
            <svg v-else class="app-sidebar__icon" viewBox="0 0 24 24" fill="currentColor" aria-hidden="true"><path d="M20 10V8h-4V4h-2v4h-4V4H8v4H4v2h4v4H4v2h4v4h2v-4h4v4h2v-4h4v-2h-4v-4h4zm-6 4h-4v-4h4v4z" /></svg>
            <span v-if="item.badge" class="app-sidebar__badge">{{ item.badge }}</span>
          </span>
          <span class="app-sidebar__label">{{ item.label }}</span>
        </RouterLink>
      </nav>
    </div>
    <div class="app-sidebar__bottom">
      <div v-if="operator" class="app-sidebar__user">
        <button type="button" class="app-sidebar__operator" :aria-label="`User profile: ${operator.name}`">{{ operator.initials }}</button>
        <div class="app-sidebar__tooltip">
          <span class="app-sidebar__tooltip-name">{{ operator.name }}</span>
          <span class="app-sidebar__tooltip-email">{{ operator.email }}</span>
          <span class="app-sidebar__tooltip-role">{{ operator.role }}</span>
        </div>
      </div>
      <button
        v-if="authenticationEnabled"
        type="button"
        class="app-sidebar__logout"
        title="Log out"
        aria-label="Log out"
        @click="handleLogout"
      >
        <svg viewBox="0 0 24 24" fill="currentColor" aria-hidden="true"><path d="M17 7l-1.41 1.41L18.17 11H8v2h10.17l-2.58 2.58L17 17l5-5zM4 5h8V3H4c-1.1 0-2 .9-2 2v14c0 1.1.9 2 2 2h8v-2H4V5z" /></svg>
      </button>
    </div>
  </aside>
</template>

<style lang="scss" scoped>
@use '@/common/styles/app-sidebar';
</style>
