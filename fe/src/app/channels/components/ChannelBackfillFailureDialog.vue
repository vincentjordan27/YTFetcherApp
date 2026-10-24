<script setup lang="ts">
import { computed } from 'vue'
import AppDialog from '@/common/components/AppDialog.vue'
import type { ChannelReviewRow } from '@/app/channels/types/channel-review'

/**
 * Failure detail for an approved channel. It shows only persisted, API-provided values: the safe
 * reason and the relevant timestamp, never raw provider, Hangfire, or database text. A scheduling
 * failure is explicitly distinguished from a backfill execution failure. It is read-only: retrying failed
 * page work belongs to the Backfill runs page.
 */
const props = defineProps<{
  channel: ChannelReviewRow | null
}>()

const emit = defineEmits<{ close: [] }>()

/** The product's automatic-failure limit; the API can never report a higher count. */
const FAILURE_LIMIT = 3

const dialogTitleId = 'channel-backfill-failure-dialog-title'

const isSchedulingFailure = computed(() => props.channel?.backfillTone === 'schedulingFailed')
const title = computed(() => (isSchedulingFailure.value ? 'Scheduling failed' : 'Backfill failed'))
</script>

<template>
  <AppDialog
    :open="channel !== null"
    :labelled-by="dialogTitleId"
    variant="channel-backfill-failure"
    @close="emit('close')"
  >
    <template #header>
      <h2 :id="dialogTitleId" class="channel-backfill-failure-dialog__title">{{ title }}</h2>
      <button
        type="button"
        class="channel-backfill-failure-dialog__close"
        aria-label="Close dialog"
        @click="emit('close')"
      >
        <svg viewBox="0 0 24 24" fill="currentColor" aria-hidden="true">
          <path d="M19 6.41L17.59 5 12 10.59 6.41 5 5 6.41 10.59 12 5 17.59 6.41 19 12 13.41 17.59 19 19 17.59 13.41 12z" />
        </svg>
      </button>
    </template>

    <template v-if="channel">
      <p class="channel-backfill-failure-dialog__channel">
        {{ channel.name }} <span>({{ channel.handle }})</span>
      </p>
      <p class="channel-backfill-failure-dialog__reason">
        {{ isSchedulingFailure ? channel.schedulingReason : channel.failureReason }}
      </p>
      <dl class="channel-backfill-failure-dialog__meta">
        <div v-if="isSchedulingFailure" class="channel-backfill-failure-dialog__meta-row">
          <dt>Scheduling attempted</dt>
          <dd>{{ channel.schedulingAt }}</dd>
        </div>
        <template v-else>
          <div class="channel-backfill-failure-dialog__meta-row">
            <dt>Last failure</dt>
            <dd>{{ channel.failureAt }}</dd>
          </div>
          <div class="channel-backfill-failure-dialog__meta-row">
            <dt>Automatic attempts</dt>
            <dd>Attempt {{ channel.failureCount ?? 0 }} of {{ FAILURE_LIMIT }}</dd>
          </div>
        </template>
      </dl>
    </template>

    <template #actions>
      <button
        type="button"
        class="channel-backfill-failure-dialog__button"
        @click="emit('close')"
      >
        Close
      </button>
    </template>
  </AppDialog>
</template>

<style lang="scss">
@use '@/app/channels/styles/channel-backfill-failure-dialog';
</style>
