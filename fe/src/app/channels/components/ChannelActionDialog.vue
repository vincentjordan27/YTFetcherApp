<script setup lang="ts">
import AppDialog from '@/common/components/AppDialog.vue'
import type { ChannelDialogKind, ChannelDialogTarget } from '@/app/channels/types/channel-review'
import { computed } from 'vue'

const props = defineProps<{
  kind: ChannelDialogKind | null
  channel: ChannelDialogTarget | null
  selectionCount?: number
  rejectionReason: string
  pending: boolean
  error?: string | null
  /** Distinguishes the pending rejection from the approved-channel rejection copy. */
  rejectionContext?: 'pending' | 'approved'
}>()

const emit = defineEmits<{
  close: []
  confirm: []
  'update:rejectionReason': [value: string]
}>()

const dialogTitleId = 'channel-action-dialog-title'

const selectedCount = computed(() => props.selectionCount ?? (props.channel === null ? 0 : 1))

const reasonInvalid = computed(
  () => props.kind === 'reject' && props.rejectionReason.trim().length === 0,
)

const confirmDisabled = computed(() => props.pending || reasonInvalid.value)

function confirmLabel(): string {
  if (props.pending) return props.kind === 'approve' ? 'Approving…' : 'Rejecting…'
  return props.kind === 'approve' ? 'Approve' : 'Reject'
}
</script>

<template>
  <AppDialog
    :open="kind !== null"
    :labelled-by="dialogTitleId"
    :busy="pending"
    variant="channel"
    @close="emit('close')"
  >
    <template #header>
      <div class="channel-action-dialog__heading">
        <span
          class="channel-action-dialog__icon"
          :class="kind === 'approve' ? 'channel-action-dialog__icon--approve' : 'channel-action-dialog__icon--reject'"
          aria-hidden="true"
        >
          <svg v-if="kind === 'approve'" class="channel-action-dialog__icon-svg" viewBox="0 0 24 24" fill="currentColor">
            <path d="M23 12l-2.44-2.79.34-3.69-3.61-.82-1.89-3.2L12 2.96 8.6 1.5 6.71 4.69 3.1 5.5l.34 3.7L1 12l2.44 2.79-.34 3.7 3.61.82L8.6 22.5l3.4-1.47 3.4 1.46 1.89-3.19 3.61-.82-.34-3.69L23 12zm-12.91 4.72l-3.8-3.81 1.48-1.48 2.32 2.33 5.85-5.87 1.48 1.48-7.33 7.35z" />
          </svg>
          <svg v-else class="channel-action-dialog__icon-svg" viewBox="0 0 24 24" fill="currentColor">
            <path d="M12 2C6.48 2 2 6.48 2 12s4.48 10 10 10 10-4.48 10-10S17.52 2 12 2zM4 12c0-4.42 3.58-8 8-8 1.85 0 3.55.63 4.9 1.69L5.69 16.9A7.9 7.9 0 014 12zm8 8c-1.85 0-3.55-.63-4.9-1.69L18.31 7.1A7.9 7.9 0 0120 12c0 4.42-3.58 8-8 8z" />
          </svg>
        </span>
        <h2 :id="dialogTitleId" class="channel-action-dialog__title">
          {{ kind === 'approve' ? 'Approve channel?' : 'Reject channel?' }}
        </h2>
      </div>
      <button type="button" class="channel-action-dialog__close" aria-label="Close dialog" :disabled="pending" @click="emit('close')">
        <svg class="channel-action-dialog__close-icon" viewBox="0 0 24 24" fill="currentColor" aria-hidden="true">
          <path d="M19 6.41L17.59 5 12 10.59 6.41 5 5 6.41 10.59 12 5 17.59 6.41 19 12 13.41 17.59 19 19 17.59 13.41 12z" />
        </svg>
      </button>
    </template>

    <template v-if="channel && selectedCount === 1">
      <div class="channel-action-dialog__target">
        <span class="channel-action-dialog__target-icon" aria-hidden="true">
          <svg class="channel-action-dialog__target-icon-svg" viewBox="0 0 24 24" fill="currentColor">
            <path d="M20 4H4c-1.1 0-2 .9-2 2v12c0 1.1.9 2 2 2h16c1.1 0 2-.9 2-2V6c0-1.1-.9-2-2-2zm-10 10.5v-5l5 2.5-5 2.5z" />
          </svg>
        </span>
        <div class="channel-action-dialog__target-meta">
          <div class="channel-action-dialog__target-line">
            <span class="channel-action-dialog__target-name">{{ channel.name }}</span>
            <span class="channel-action-dialog__target-handle">({{ channel.handle }})</span>
          </div>
          <div class="channel-action-dialog__target-status">
            <span class="channel-action-dialog__target-dot" aria-hidden="true"></span>
            <span>Valid YouTube channel ID</span>
          </div>
        </div>
      </div>

      <p v-if="kind === 'approve'" class="channel-action-dialog__consequence">
        This channel will be backfilled for eligible short-form videos and monitored for new uploads.
      </p>
      <p v-else-if="rejectionContext === 'approved'" class="channel-action-dialog__consequence">
        This channel will stop backfilling and will no longer add new videos.
      </p>
      <p v-else-if="kind === 'reject'" class="channel-action-dialog__consequence">
        This channel will be removed from review.
      </p>
      <label v-if="kind === 'reject'" class="channel-action-dialog__reason">
        <span class="channel-action-dialog__reason-label">Rejection reason</span>
        <textarea
          class="channel-action-dialog__reason-input"
          :value="rejectionReason"
          rows="3"
          maxlength="500"
          placeholder="e.g. irrelevant content / not short-form craft"
          :disabled="pending"
          @input="emit('update:rejectionReason', ($event.target as HTMLTextAreaElement).value)"
        />
      </label>
      <p v-if="error" class="channel-action-dialog__error" role="alert">{{ error }}</p>
    </template>

    <template v-else-if="selectedCount > 1">
      <p class="channel-action-dialog__consequence">
        {{ selectedCount }} selected channels will be {{ kind === 'approve' ? 'approved and scheduled for backfill.' : 'rejected with the same reason.' }}
      </p>
      <label v-if="kind === 'reject'" class="channel-action-dialog__reason">
        <span class="channel-action-dialog__reason-label">Rejection reason</span>
        <textarea
          class="channel-action-dialog__reason-input"
          :value="rejectionReason"
          rows="3"
          maxlength="500"
          placeholder="e.g. irrelevant content / not short-form craft"
          :disabled="pending"
          @input="emit('update:rejectionReason', ($event.target as HTMLTextAreaElement).value)"
        />
      </label>
      <p v-if="error" class="channel-action-dialog__error" role="alert">{{ error }}</p>
    </template>

    <template #actions>
      <button type="button" class="channel-action-dialog__button channel-action-dialog__button--secondary" :disabled="pending" @click="emit('close')">
        Cancel
      </button>
      <button
        type="button"
        class="channel-action-dialog__button"
        :class="kind === 'approve' ? 'channel-action-dialog__button--approve' : 'channel-action-dialog__button--reject'"
        :disabled="confirmDisabled"
        @click="emit('confirm')"
      >
        <svg v-if="pending" class="channel-action-dialog__button-icon channel-action-dialog__spinner" viewBox="0 0 24 24" fill="currentColor" aria-hidden="true">
          <path d="M12 4V1L8 5l4 4V6c3.31 0 6 2.69 6 6 0 1.01-.25 1.97-.7 2.8l1.46 1.46A7.93 7.93 0 0020 12c0-4.42-3.58-8-8-8zm0 14c-3.31 0-6-2.69-6-6 0-1.01.25-1.97.7-2.8L5.24 7.74A7.93 7.93 0 004 12c0 4.42 3.58 8 8 8v3l4-4-4-4v3z" />
        </svg>
        <svg v-else-if="kind === 'approve'" class="channel-action-dialog__button-icon" viewBox="0 0 24 24" fill="currentColor" aria-hidden="true">
          <path d="M9 16.17L4.83 12l-1.42 1.41L9 19 21 7l-1.41-1.41z" />
        </svg>
        <svg v-else class="channel-action-dialog__button-icon" viewBox="0 0 24 24" fill="currentColor" aria-hidden="true">
          <path d="M19 6.41L17.59 5 12 10.59 6.41 5 5 6.41 10.59 12 5 17.59 6.41 19 12 13.41 17.59 19 19 17.59 13.41 12z" />
        </svg>
        <span>{{ confirmLabel() }}</span>
      </button>
    </template>
  </AppDialog>
</template>

<style lang="scss">
@use '@/app/channels/styles/channel-action-dialog';
</style>
