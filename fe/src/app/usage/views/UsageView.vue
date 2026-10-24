<script setup lang="ts">
import AdminShell from '@/common/layouts/AdminShell.vue'
import { useTodayUsage } from '@/app/usage/composables/use-today-usage'

const { usage, state, stateMessage, retry } = useTodayUsage()

const pacificDateTimeFormatter = new Intl.DateTimeFormat('en-GB', {
  timeZone: 'America/Los_Angeles',
  day: '2-digit',
  month: 'short',
  year: 'numeric',
  hour: '2-digit',
  minute: '2-digit',
  hour12: false,
})

// The marker is a page-entry snapshot of Pacific wall-clock time. It is display-only, is never the
// quota-day state, and must not use the browser's local timezone.
const pacificNowLabel = `${pacificDateTimeFormatter.format(new Date())} Pacific`
</script>

<template>
  <AdminShell>
    <section class="usage-page">
      <div class="usage-page__body">
        <header class="usage-page__header">
          <h1 class="usage-page__title">YouTube API usage</h1>
          <span class="usage-page__clock">{{ pacificNowLabel }}</span>
        </header>

        <div v-if="state === 'loading'" class="usage-page__feedback" role="status">
          <span class="usage-page__indicator" aria-hidden="true" />
          <span class="usage-page__message">{{ stateMessage }}</span>
        </div>

        <div v-else-if="state === 'error'" class="usage-page__feedback" role="alert">
          <span class="usage-page__message">{{ stateMessage }}</span>
          <button type="button" class="usage-page__action" @click="retry">Retry</button>
        </div>

        <template v-else-if="usage">
          <section class="usage-breakdown" aria-labelledby="usage-breakdown-heading">
            <h2 id="usage-breakdown-heading" class="usage-breakdown__heading">Provider quota buckets</h2>
            <div v-for="bucket in usage.buckets" :key="bucket.id" class="usage-breakdown__row">
              <span class="usage-breakdown__name">
                {{ bucket.label }}
                <span class="usage-breakdown__hint">{{ bucket.hint }}</span>
              </span>
              <span class="usage-breakdown__value">
                <strong class="usage-breakdown__used">{{ bucket.usedLabel }}</strong>
                <span class="usage-breakdown__limit">/ {{ bucket.limitLabel }}</span>
                <span v-if="bucket.exhaustedLabel" class="usage-breakdown__exhausted">{{ bucket.exhaustedLabel }}</span>
              </span>
            </div>

            <div class="usage-breakdown__row">
              <span class="usage-breakdown__name">
                First-page reservations
                <span class="usage-breakdown__hint">{{ usage.firstPageCap.hint }}</span>
              </span>
              <span class="usage-breakdown__value">
                <strong class="usage-breakdown__used">{{ usage.firstPageCap.usedLabel }}</strong>
                <span class="usage-breakdown__limit">/ {{ usage.firstPageCap.limitLabel }}</span>
              </span>
            </div>
          </section>
        </template>
      </div>
    </section>
  </AdminShell>
</template>

<style lang="scss" scoped>
@use '@/app/usage/styles/usage';
</style>
