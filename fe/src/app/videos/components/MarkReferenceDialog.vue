<script setup lang="ts">
import AppDialog from '@/common/components/AppDialog.vue'

defineProps<{
  open: boolean
  selectedCount: number
  pending: boolean
  error?: string | null
}>()

const emit = defineEmits<{
  close: []
  confirm: []
}>()

const dialogTitleId = 'mark-reference-dialog-title'
</script>

<template>
  <AppDialog
    :open="open"
    :labelled-by="dialogTitleId"
    :busy="pending"
    variant="video-reference"
    @close="emit('close')"
  >
    <template #header>
      <h2 :id="dialogTitleId" class="mark-reference-dialog__title">Mark as reference</h2>
      <button
        type="button"
        class="mark-reference-dialog__close"
        aria-label="Close dialog"
        :disabled="pending"
        @click="emit('close')"
      >
        <svg class="mark-reference-dialog__close-icon" viewBox="0 0 24 24" fill="currentColor" aria-hidden="true">
          <path d="M19 6.41L17.59 5 12 10.59 6.41 5 5 6.41 10.59 12 5 17.59 6.41 19 12 13.41 17.59 19 19 17.59 13.41 12z" />
        </svg>
      </button>
    </template>

    <p class="mark-reference-dialog__body">
      The selected videos will be marked as reference and will no longer receive daily stats updates.
    </p>
    <p class="mark-reference-dialog__selection">{{ selectedCount }} videos selected.</p>
    <p v-if="error" class="mark-reference-dialog__error" role="alert">{{ error }}</p>

    <template #actions>
      <button
        type="button"
        class="mark-reference-dialog__button mark-reference-dialog__button--secondary"
        :disabled="pending"
        @click="emit('close')"
      >
        Cancel
      </button>
      <button
        type="button"
        class="mark-reference-dialog__button mark-reference-dialog__button--primary"
        :disabled="pending"
        @click="emit('confirm')"
      >
        <span>{{ pending ? 'Marking…' : 'Mark as reference' }}</span>
      </button>
    </template>
  </AppDialog>
</template>

<style lang="scss">
@use '@/app/videos/styles/mark-reference-dialog';
</style>
