<script setup lang="ts">
withDefaults(
  defineProps<{
    count?: number
    /** True while this row's evidence panel is open. Drives the highlight only. */
    active?: boolean
  }>(),
  { active: false },
)
</script>

<template>
  <span class="channel-evidence-summary" :class="{ 'channel-evidence-summary--active': active }">
    <svg class="channel-evidence-summary__icon" viewBox="0 0 24 24" fill="currentColor" aria-hidden="true">
      <path d="M18 4l2 4h-3l-2-4h-2l2 4h-3l-2-4H8l2 4H7L5 4H4c-1.1 0-1.99.9-1.99 2L2 18c0 1.1.9 2 2 2h16c1.1 0 2-.9 2-2V4h-4z" />
    </svg>
    <span>{{ count }} shorts</span>
  </span>
</template>

<style lang="scss" scoped>
@use '@/app/channels/styles/channel-evidence-summary';
</style>
