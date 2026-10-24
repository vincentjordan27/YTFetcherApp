<script setup lang="ts">
import { computed } from 'vue'
import type { ChannelBackfillTone } from '@/app/channels/types/channel-review'

const props = defineProps<{ label?: string; tone?: ChannelBackfillTone }>()
const emit = defineEmits<{ openFailure: [] }>()

const isPill = computed(() => props.tone === 'completed' || props.tone === 'running' || props.tone === 'scheduling')
const isFailure = computed(() => props.tone === 'failed' || props.tone === 'schedulingFailed')
const failureModifier = computed(() => (props.tone === 'schedulingFailed' ? 'scheduling-failed' : 'failed'))
</script>

<template>
  <!-- A failed backfill is the only interactive element in this cell and opens its detail dialog. -->
  <button
    v-if="isFailure"
    type="button"
    class="channel-backfill-status-pill"
    :class="`channel-backfill-status-pill--${failureModifier}`"
    :title="`${label} backfill. Open the failure details.`"
    @click.stop="emit('openFailure')"
  >
    <svg class="channel-backfill-status-pill__icon" viewBox="0 0 24 24" fill="currentColor" aria-hidden="true">
      <path d="M1 21h22L12 2 1 21zm12-3h-2v-2h2v2zm0-4h-2v-4h2v4z" />
    </svg>
    <span>{{ label }}</span>
  </button>
  <span v-else-if="isPill" class="channel-backfill-status-pill" :class="`channel-backfill-status-pill--${tone}`">
    <svg class="channel-backfill-status-pill__icon" viewBox="0 0 24 24" fill="currentColor" aria-hidden="true">
      <path
        v-if="tone === 'completed'"
        d="M12 2C6.48 2 2 6.48 2 12s4.48 10 10 10 10-4.48 10-10S17.52 2 12 2zm-2 15l-5-5 1.41-1.41L10 14.17l7.59-7.59L19 8l-9 9z"
      />
      <path
        v-else-if="tone === 'scheduling'"
        d="M12 2C6.48 2 2 6.48 2 12s4.48 10 10 10 10-4.48 10-10S17.52 2 12 2zm0 18c-4.41 0-8-3.59-8-8s3.59-8 8-8 8 3.59 8 8-3.59 8-8 8zm.5-13H11v6l5.25 3.15.75-1.23-4.5-2.67z"
      />
      <path v-else d="M12 4V1L8 5l4 4V6c3.31 0 6 2.69 6 6 0 1.01-.25 1.97-.7 2.8l1.46 1.46A7.93 7.93 0 0020 12c0-4.42-3.58-8-8-8zm0 14c-3.31 0-6-2.69-6-6 0-1.01.25-1.97.7-2.8L5.24 7.74A7.93 7.93 0 004 12c0 4.42 3.58 8 8 8v3l4-4-4-4v3z" />
    </svg>
    <span>{{ label }}</span>
  </span>
  <span v-else class="channel-backfill-status">
    <svg class="channel-backfill-status__icon" viewBox="0 0 24 24" fill="currentColor" aria-hidden="true">
      <path d="M6 2v6h.01L6 8.01 10 12l-4 4 .01.01H6V22h12v-5.99h-.01L18 16l-4-4 4-3.99-.01-.01H18V2H6zm10 14.5V20H8v-3.5l4-4 4 4zm-4-5l-4-4V4h8v3.5l-4 4z" />
    </svg>
    <span>{{ label }}</span>
  </span>
</template>

<style lang="scss" scoped>
@use '@/app/channels/styles/channel-backfill-status';
</style>
