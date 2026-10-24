<script setup lang="ts">
import AppDialog from '@/common/components/AppDialog.vue'

defineProps<{
  open: boolean
  channelReference: string
  pending: boolean
  error?: string | null
}>()

const emit = defineEmits<{
  close: []
  submit: []
  'update:channelReference': [value: string]
}>()

const dialogTitleId = 'manual-channel-dialog-title'
</script>

<template>
  <AppDialog
    :open="open"
    :labelled-by="dialogTitleId"
    :busy="pending"
    variant="manual-channel"
    @close="emit('close')"
  >
    <template #header>
      <h2 :id="dialogTitleId" class="manual-channel-dialog__title">Add a YouTube channel</h2>
      <button
        type="button"
        class="manual-channel-dialog__close"
        aria-label="Close dialog"
        :disabled="pending"
        @click="emit('close')"
      >
        <svg class="manual-channel-dialog__close-icon" viewBox="0 0 24 24" fill="currentColor" aria-hidden="true">
          <path d="M19 6.41L17.59 5 12 10.59 6.41 5 5 6.41 10.59 12 5 17.59 6.41 19 12 13.41 17.59 19 19 17.59 13.41 12z" />
        </svg>
      </button>
    </template>

    <label class="manual-channel-dialog__field">
      <span class="manual-channel-dialog__label">YouTube handle</span>
      <input
        class="manual-channel-dialog__input"
        type="text"
        placeholder="@channelhandle"
        autocomplete="off"
        spellcheck="false"
        :value="channelReference"
        :disabled="pending"
        @input="emit('update:channelReference', ($event.target as HTMLInputElement).value)"
      />
    </label>
    <p v-if="error" class="manual-channel-dialog__error" role="alert">{{ error }}</p>

    <template #actions>
      <button
        type="button"
        class="manual-channel-dialog__button manual-channel-dialog__button--secondary"
        :disabled="pending"
        @click="emit('close')"
      >
        Cancel
      </button>
      <button
        type="button"
        class="manual-channel-dialog__button manual-channel-dialog__button--primary"
        :disabled="pending || channelReference.trim().length === 0"
        @click="emit('submit')"
      >
        <span>{{ pending ? 'Adding…' : 'Add and approve' }}</span>
      </button>
    </template>
  </AppDialog>
</template>

<style lang="scss">
@use '@/app/channels/styles/manual-channel-dialog';
</style>
