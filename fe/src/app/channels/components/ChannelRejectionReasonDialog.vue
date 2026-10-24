<script setup lang="ts">
import AppDialog from '@/common/components/AppDialog.vue'
import type { ChannelReviewRow } from '@/app/channels/types/channel-review'

defineProps<{ channel: ChannelReviewRow | null }>()

const emit = defineEmits<{ close: [] }>()
const dialogTitleId = 'channel-rejection-reason-dialog-title'
</script>

<template>
  <AppDialog
    :open="channel !== null"
    :labelled-by="dialogTitleId"
    variant="channel-rejection-reason"
    @close="emit('close')"
  >
    <template #header>
      <h2 :id="dialogTitleId" class="channel-rejection-reason-dialog__title">Rejection reason</h2>
      <button type="button" class="channel-rejection-reason-dialog__close" aria-label="Close dialog" @click="emit('close')">
        <svg viewBox="0 0 24 24" fill="currentColor" aria-hidden="true">
          <path d="M19 6.41L17.59 5 12 10.59 6.41 5 5 6.41 10.59 12 5 17.59 6.41 19 12 13.41 17.59 19 19 17.59 13.41 12z" />
        </svg>
      </button>
    </template>

    <template v-if="channel">
      <p class="channel-rejection-reason-dialog__channel">{{ channel.name }} <span>({{ channel.handle }})</span></p>
      <p class="channel-rejection-reason-dialog__content">{{ channel.reasonTitle }}</p>
    </template>

    <template #actions>
      <button type="button" class="channel-rejection-reason-dialog__button" @click="emit('close')">Close</button>
    </template>
  </AppDialog>
</template>

<style lang="scss">
@use '@/app/channels/styles/channel-rejection-reason-dialog';
</style>
