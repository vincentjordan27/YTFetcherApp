<script setup lang="ts">
import AppDialog from '@/common/components/AppDialog.vue'

defineProps<{
  isSubmitting: boolean
  errorMessage: string | null
}>()

const emit = defineEmits<{ close: []; confirm: [] }>()
</script>

<template>
  <AppDialog :open="true" labelled-by="retry-backfill-title" variant="keyword-disable" @close="emit('close')">
    <div class="backfill-runs-page__dialog-body">
      <h2 id="retry-backfill-title" class="backfill-runs-page__dialog-title">Retry backfill?</h2>
      <p class="backfill-runs-page__dialog-text">This resumes the channel from the page that failed when quota is available.</p>
      <p v-if="errorMessage" class="backfill-runs-page__dialog-error" role="alert">{{ errorMessage }}</p>
    </div>

    <template #actions>
      <button type="button" class="backfill-runs-page__btn-refresh" :disabled="isSubmitting" @click="emit('close')">Cancel</button>
      <button type="button" class="backfill-runs-page__btn-retry" :disabled="isSubmitting" @click="emit('confirm')">
        Retry backfill
      </button>
    </template>
  </AppDialog>
</template>
