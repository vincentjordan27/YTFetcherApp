<script setup lang="ts">
import AdminShell from '@/common/layouts/AdminShell.vue'
import BackfillRunDetailDialog from '@/app/backfill-runs/components/BackfillRunDetailDialog.vue'
import BackfillRunTable from '@/app/backfill-runs/components/BackfillRunTable.vue'
import RetryBackfillDialog from '@/app/backfill-runs/components/RetryBackfillDialog.vue'
import {
  BACKFILL_RUN_STATUS_OPTIONS,
  BACKFILL_RUN_TRIGGER_OPTIONS,
  useBackfillRuns,
} from '@/app/backfill-runs/composables/useBackfillRuns'
import '@/app/backfill-runs/styles/backfill-runs.scss'

const backfillRuns = useBackfillRuns()
</script>

<template>
  <AdminShell>
    <div class="backfill-runs-page">
      <h1 class="backfill-runs-page__title">Backfill runs</h1>
      <div class="backfill-runs-page__main">
        <BackfillRunTable
          :rows="backfillRuns.rows.value"
          :pagination="backfillRuns.pagination.value"
          :table-state="backfillRuns.tableState.value"
          :search-query="backfillRuns.searchInput.value"
          :status-filter="backfillRuns.statusFilter.value"
          :trigger-filter="backfillRuns.triggerFilter.value"
          :date-from-filter="backfillRuns.dateFromFilter.value"
          :date-to-filter="backfillRuns.dateToFilter.value"
          :items-per-page="String(backfillRuns.itemsPerPage.value)"
          :page="backfillRuns.currentPage.value"
          :status-options="BACKFILL_RUN_STATUS_OPTIONS"
          :trigger-options="BACKFILL_RUN_TRIGGER_OPTIONS"
          @update:search-query="backfillRuns.updateSearchInput"
          @update:status-filter="backfillRuns.applyStatusFilter"
          @update:trigger-filter="backfillRuns.applyTriggerFilter"
          @update:date-from-filter="backfillRuns.applyDateFromFilter"
          @update:date-to-filter="backfillRuns.applyDateToFilter"
          @update:items-per-page="backfillRuns.setItemsPerPage"
          @update:page="backfillRuns.goToPage"
          @refresh="backfillRuns.refresh"
          @open-row="backfillRuns.openRow"
          @retry="backfillRuns.requestRetry"
        />

        <BackfillRunDetailDialog
          v-if="backfillRuns.dialogKind.value === 'detail' && backfillRuns.selectedRow.value !== null"
          :run="backfillRuns.selectedRow.value"
          @close="backfillRuns.closeDialog"
        />

        <RetryBackfillDialog
          v-if="backfillRuns.dialogKind.value === 'retry'"
          :is-submitting="backfillRuns.isRetrying.value"
          :error-message="backfillRuns.retryError.value"
          @close="backfillRuns.closeDialog"
          @confirm="backfillRuns.confirmRetry"
        />
      </div>
    </div>
  </AdminShell>
</template>
