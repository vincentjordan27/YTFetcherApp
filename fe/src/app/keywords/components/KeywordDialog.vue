<script setup lang="ts">
import AppDialog from '@/common/components/AppDialog.vue'
import type { KeywordDialogKind, KeywordListItem } from '@/app/keywords/types/keyword.types'

defineProps<{
  kind: KeywordDialogKind
  keyword: KeywordListItem | null
  term: string
  isSubmitting: boolean
  mutationError: string | null
  canSubmit: boolean
}>()

const emit = defineEmits<{
  close: []
  'update:term': [value: string]
  add: []
  disable: []
  remove: []
}>()
</script>

<template>
  <AppDialog v-if="kind === 'add'" :open="true" labelled-by="add-keyword-title" variant="keyword-add" @close="emit('close')">
    <template #header>
      <div class="keyword-dialog__header-content">
        <div class="keyword-dialog__heading">
          <svg class="keyword-dialog__heading-icon" viewBox="0 0 24 24" fill="currentColor" aria-hidden="true"><path d="M12 2C6.48 2 2 6.48 2 12s4.48 10 10 10 10-4.48 10-10S17.52 2 12 2zm5 11h-4v4h-2v-4H7v-2h4V7h2v4h4v2z" /></svg>
          <h2 id="add-keyword-title" class="keyword-dialog__title">Add discovery keyword</h2>
        </div>
        <button type="button" class="keyword-dialog__close" aria-label="Close dialog" @click="emit('close')"><svg class="keyword-icon" viewBox="0 0 24 24" fill="currentColor" aria-hidden="true"><path d="M19 6.41L17.59 5 12 10.59 6.41 5 5 6.41 10.59 12 5 17.59 6.41 19 12 13.41 17.59 19 19 17.59 13.41 12z" /></svg></button>
      </div>
    </template>
    <form id="add-keyword-form" class="keyword-dialog__form" @submit.prevent="emit('add')">
      <label class="keyword-dialog__field-label" for="keyword-term">Keyword</label>
      <div class="keyword-dialog__field">
        <svg class="keyword-dialog__field-icon" viewBox="0 0 24 24" fill="currentColor" aria-hidden="true"><path d="M20 10V8h-4V4h-2v4h-4V4H8v4H4v2h4v4H4v2h4v4h2v-4h4v4h2v-4h4v-2h-4v-4h4zm-6 4h-4v-4h4v4z" /></svg>
        <input id="keyword-term" :value="term" class="keyword-dialog__field-input" type="text" placeholder="e.g. tokyo stationery haul #shorts" required @input="emit('update:term', ($event.target as HTMLInputElement).value)" />
      </div>
      <p class="keyword-dialog__field-help">Used in the next channel discovery run.</p>
      <p v-if="mutationError" class="keyword-dialog__error" role="alert">{{ mutationError }}</p>
    </form>
    <template #actions>
      <button type="button" class="keyword-dialog__button keyword-dialog__button--cancel" :disabled="isSubmitting" @click="emit('close')">Cancel</button>
      <button type="submit" form="add-keyword-form" class="keyword-dialog__button keyword-dialog__button--primary" :disabled="isSubmitting || !canSubmit">Add keyword</button>
    </template>
  </AppDialog>

  <AppDialog v-else-if="kind === 'disable'" :open="true" labelled-by="disable-keyword-title" variant="keyword-disable" @close="emit('close')">
    <div class="keyword-dialog__confirm-lead">
      <div class="keyword-dialog__confirm-icon"><svg class="keyword-icon" viewBox="0 0 24 24" fill="currentColor" aria-hidden="true"><path d="M1 21h22L12 2 1 21zm12-3h-2v-2h2v2zm0-4h-2v-4h2v4z" /></svg></div>
      <div class="keyword-dialog__confirm-copy">
        <h2 id="disable-keyword-title" class="keyword-dialog__title">Disable keyword?</h2>
        <p class="keyword-dialog__confirm-text">Discovery will no longer use this keyword. Existing channels and videos stay unchanged.</p>
        <div class="keyword-dialog__confirm-target">keyword: <span class="keyword-dialog__confirm-target-value">"{{ keyword?.term }}"</span></div>
        <p v-if="mutationError" class="keyword-dialog__error" role="alert">{{ mutationError }}</p>
      </div>
    </div>
    <template #actions>
      <button type="button" class="keyword-dialog__button keyword-dialog__button--cancel" :disabled="isSubmitting" @click="emit('close')">Cancel</button>
      <button type="button" class="keyword-dialog__button keyword-dialog__button--danger" :disabled="isSubmitting" @click="emit('disable')">Disable keyword</button>
    </template>
  </AppDialog>

  <AppDialog v-else-if="kind === 'remove'" :open="true" labelled-by="remove-keyword-title" variant="keyword-delete" @close="emit('close')">
    <template #header>
      <div class="keyword-dialog__header-content">
        <div class="keyword-dialog__heading"><span class="keyword-dialog__delete-icon"><svg class="keyword-icon" viewBox="0 0 24 24" fill="currentColor" aria-hidden="true"><path d="M6 19c0 1.1.9 2 2 2h8c1.1 0 2-.9 2-2V7H6v12zM19 4h-3.5l-1-1h-5l-1 1H5v2h14V4z" /></svg></span><h2 id="remove-keyword-title" class="keyword-dialog__title">Remove keyword?</h2></div>
        <button type="button" class="keyword-dialog__close" aria-label="Close dialog" @click="emit('close')"><svg class="keyword-icon" viewBox="0 0 24 24" fill="currentColor" aria-hidden="true"><path d="M19 6.41L17.59 5 12 10.59 6.41 5 5 6.41 10.59 12 5 17.59 6.41 19 12 13.41 17.59 19 19 17.59 13.41 12z" /></svg></button>
      </div>
    </template>
    <div class="keyword-dialog__delete-body">
      <div class="keyword-dialog__delete-target">
        <span class="keyword-dialog__delete-target-icon"><svg class="keyword-icon" viewBox="0 0 24 24" fill="currentColor" aria-hidden="true"><path d="M20 10V8h-4V4h-2v4h-4V4H8v4H4v2h4v4H4v2h4v4h2v-4h4v4h2v-4h4v-2h-4v-4h4zm-6 4h-4v-4h4v4z" /></svg></span>
        <div class="keyword-dialog__delete-target-meta"><span class="keyword-dialog__delete-target-name">{{ keyword?.term }}</span><span class="keyword-dialog__delete-target-status"><span class="keyword-dialog__delete-target-dot" />Discovery term</span></div>
      </div>
      <p class="keyword-dialog__delete-consequence">This removes the keyword from discovery. Existing channels and videos stay unchanged.</p>
      <p v-if="mutationError" class="keyword-dialog__error" role="alert">{{ mutationError }}</p>
    </div>
    <template #actions>
      <button type="button" class="keyword-dialog__button keyword-dialog__button--cancel" :disabled="isSubmitting" @click="emit('close')">Cancel</button>
      <button type="button" class="keyword-dialog__button keyword-dialog__button--danger" :disabled="isSubmitting" @click="emit('remove')">Remove</button>
    </template>
  </AppDialog>
</template>

<style lang="scss">
@use '../styles/keyword-dialog';
</style>
