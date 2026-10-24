<script setup lang="ts">
import type { ActiveVideoTableState } from '@/app/videos/types/active-video.types'

defineProps<{
  colspan: number
  message: string
  state: ActiveVideoTableState
}>()

const emit = defineEmits<{ retry: [] }>()
</script>

<template>
  <tr class="active-video-state-row">
    <td :colspan="colspan" class="active-video-state-row__cell">
      <span class="active-video-state-row__content" :class="{ 'active-video-state-row__content--busy': state === 'loading' }">
        <span v-if="state === 'loading'" class="active-video-state-row__indicator" aria-hidden="true" />
        <span class="active-video-state-row__message">{{ message }}</span>
        <button
          v-if="state === 'error'"
          type="button"
          class="active-video-state-row__action"
          @click="emit('retry')"
        >
          Retry
        </button>
      </span>
    </td>
  </tr>
</template>

<style lang="scss" scoped>
@use '@/app/videos/styles/active-video-table';
</style>
