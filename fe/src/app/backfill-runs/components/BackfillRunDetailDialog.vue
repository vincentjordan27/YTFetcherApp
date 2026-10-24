<script setup lang="ts">
import AppDialog from '@/common/components/AppDialog.vue'
import type { BackfillRunRow } from '@/app/backfill-runs/types/backfill-run.types'

defineProps<{ run: BackfillRunRow }>()

const emit = defineEmits<{ close: [] }>()
</script>

<template>
  <AppDialog :open="true" labelled-by="backfill-run-detail-title" variant="keyword-disable" @close="emit('close')">
    <template #header>
      <div class="backfill-runs-page__dialog-header">
        <h2 id="backfill-run-detail-title" class="backfill-runs-page__dialog-title">
          {{ run.statusTone === 'quota' ? 'Quota limit reached' : 'Backfill run failed' }}
        </h2>
        <button type="button" class="backfill-runs-page__dialog-close" aria-label="Close dialog" @click="emit('close')">Close</button>
      </div>
    </template>

    <div class="backfill-runs-page__dialog-body">
      <dl class="backfill-runs-page__detail-list">
        <dt>Channel</dt>
        <dd>{{ run.channelName }} <span class="backfill-runs-page__channel-id">{{ run.youtubeChannelId }}</span></dd>
        <dt>Page</dt>
        <dd>{{ run.pageNumber }}</dd>
        <dt>Trigger</dt>
        <dd>{{ run.triggerLabel }}</dd>
        <dt>Started</dt>
        <dd>{{ run.startedLabel }}</dd>
        <dt>Finished</dt>
        <dd>{{ run.finishedLabel }}</dd>
        <template v-if="run.quotaLimitBucketLabel">
          <dt>Bucket</dt>
          <dd>{{ run.quotaLimitBucketLabel }}</dd>
        </template>
        <template v-if="run.failureCountAtTime !== null">
          <dt>Failure attempts</dt>
          <dd>{{ run.failureCountAtTime }}</dd>
        </template>
      </dl>

      <div class="backfill-runs-page__detail-reason">
        <span class="backfill-runs-page__detail-reason-label">Reason</span>
        <p class="backfill-runs-page__detail-reason-text">{{ run.errorLabel }}</p>
      </div>
    </div>

    <template #actions>
      <button type="button" class="backfill-runs-page__btn-refresh" @click="emit('close')">Close</button>
    </template>
  </AppDialog>
</template>
