<script setup lang="ts">
import { useToast } from '@/common/composables/useToast'

const { currentToast, dismissToast } = useToast()
</script>

<template>
  <Teleport to="body">
    <div v-if="currentToast" class="app-toast" :class="[`app-toast--${currentToast.tone}`, currentToast.position === 'bottom-right' ? 'app-toast--bottom' : '']" role="status" aria-live="polite">
      <span class="app-toast__icon" aria-hidden="true">
        <svg v-if="currentToast.tone === 'success'" viewBox="0 0 24 24" fill="currentColor"><path d="M12 2a10 10 0 1 0 0 20 10 10 0 0 0 0-20Zm-2 14-4-4 1.41-1.41L10 13.17l6.59-6.58L18 8l-8 8Z" /></svg>
        <svg v-else-if="currentToast.tone === 'failure'" viewBox="0 0 24 24" fill="currentColor"><path d="M12 2a10 10 0 1 0 0 20 10 10 0 0 0 0-20Zm1 15h-2v-2h2v2Zm0-4h-2V7h2v6Z" /></svg>
        <svg v-else viewBox="0 0 24 24" fill="currentColor"><path d="M11 17h2v-6h-2v6Zm1-15a10 10 0 1 0 0 20 10 10 0 0 0 0-20Zm0 18a8 8 0 1 1 0-16 8 8 0 0 1 0 16Zm-1-11h2V7h-2v2Z" /></svg>
      </span>
      <span class="app-toast__message">{{ currentToast.message }}</span>
      <button type="button" class="app-toast__dismiss" aria-label="Dismiss notification" title="Dismiss notification" @click="dismissToast">
        <svg viewBox="0 0 24 24" fill="currentColor" aria-hidden="true"><path d="m18.3 5.71-1.41-1.42L12 9.17 7.11 4.29 5.7 5.71 10.59 10.6 5.7 15.49l1.41 1.42L12 12l4.89 4.91 1.41-1.42-4.89-4.89 4.89-4.89Z" /></svg>
      </button>
    </div>
  </Teleport>
</template>

<style lang="scss" scoped>
@use '@/common/styles/app-toast';
</style>
