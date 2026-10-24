<script setup lang="ts">
import { authCopy } from '../data/auth-copy'

defineProps<{
  isSubmitting: boolean
  errorMessage: string | null
  unavailable: boolean
}>()

defineEmits<{
  signIn: []
  retry: []
}>()
</script>

<template>
  <div class="login-panel">
    <template v-if="unavailable">
      <p class="login-panel__error" role="alert">{{ authCopy.errors.unavailable }}</p>
      <div class="login-panel__actions">
        <button type="button" class="login-panel__submit" @click="$emit('retry')">
          <span>{{ authCopy.login.retryLabel }}</span>
        </button>
      </div>
    </template>
    <template v-else>
      <p class="login-panel__notice">{{ authCopy.login.notice }}</p>
      <p v-if="errorMessage" class="login-panel__error" role="alert">{{ errorMessage }}</p>
      <div class="login-panel__actions">
        <button
          type="button"
          class="login-panel__submit"
          :disabled="isSubmitting"
          :aria-busy="isSubmitting"
          @click="$emit('signIn')"
        >
          <template v-if="isSubmitting">
            <svg class="app-icon spinner" viewBox="0 0 24 24" fill="currentColor" aria-hidden="true"><path d="M12 4V1L8 5l4 4V6c3.31 0 6 2.69 6 6 0 1.01-.25 1.97-.7 2.8l1.46 1.46A7.93 7.93 0 0020 12c0-4.42-3.58-8-8-8zm0 14c-3.31 0-6-2.69-6-6 0-1.01.25-1.97.7-2.8L5.24 7.74A7.93 7.93 0 004 12c0 4.42 3.58 8 8 8v3l4-4-4-4v3z" /></svg>
            <span>{{ authCopy.login.submittingLabel }}</span>
          </template>
          <template v-else>
            <span>{{ authCopy.login.submitLabel }}</span>
            <svg class="app-icon" viewBox="0 0 24 24" fill="currentColor" aria-hidden="true"><path d="M12 4l-1.41 1.41L16.17 11H4v2h12.17l-5.58 5.59L12 20l8-8z" /></svg>
          </template>
        </button>
      </div>
    </template>
  </div>
</template>

<style lang="scss" scoped src="../styles/login.scss"></style>
