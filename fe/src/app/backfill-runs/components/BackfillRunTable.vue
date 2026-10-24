<script setup lang="ts">
import { computed } from 'vue'
import AppDataTable from '@/common/components/AppDataTable.vue'
import AppPagination from '@/common/components/AppPagination.vue'
import BackfillRunStateRow from '@/app/backfill-runs/components/BackfillRunStateRow.vue'
import type {
  BackfillRunPagination,
  BackfillRunRow,
  BackfillRunTableState,
} from '@/app/backfill-runs/types/backfill-run.types'

const props = defineProps<{
  rows: readonly BackfillRunRow[]
  pagination: BackfillRunPagination
  tableState: BackfillRunTableState
  searchQuery: string
  statusFilter: string
  triggerFilter: string
  dateFromFilter: string
  dateToFilter: string
  itemsPerPage: string
  page: number
  statusOptions: readonly string[]
  triggerOptions: readonly string[]
}>()

const emit = defineEmits<{
  'update:searchQuery': [value: string]
  'update:statusFilter': [value: string]
  'update:triggerFilter': [value: string]
  'update:dateFromFilter': [value: string]
  'update:dateToFilter': [value: string]
  'update:itemsPerPage': [value: string]
  'update:page': [value: number]
  refresh: []
  openRow: [row: BackfillRunRow]
  retry: [row: BackfillRunRow]
}>()

const PAGE_SIZE_CHOICES: readonly number[] = [10, 25, 50, 100]

// The initial page size is derived from the viewport, so it is not always one of the fixed choices. Without the
// current value in the list, a <select> bound to it has no matching option and renders completely blank.
const pageSizeOptions = computed<number[]>(() => {
  const current = Number(props.itemsPerPage)

  if (!Number.isFinite(current) || current <= 0 || PAGE_SIZE_CHOICES.includes(current)) {
    return [...PAGE_SIZE_CHOICES]
  }

  return [...PAGE_SIZE_CHOICES, current].sort((left, right) => left - right)
})

function label(value: string): string {
  return value
    .toLowerCase()
    .split('_')
    .map((part) => part.charAt(0).toUpperCase() + part.slice(1))
    .join(' ')
}
</script>

<template>
  <section class="backfill-runs-page__ledger" aria-label="Backfill runs">
    <div class="backfill-runs-page__toolbar">
      <div class="backfill-runs-page__search">
        <input
          :value="searchQuery"
          class="backfill-runs-page__search-input"
          type="search"
          placeholder="Search channel or channel ID..."
          aria-label="Search backfill runs"
          @input="emit('update:searchQuery', ($event.target as HTMLInputElement).value)"
        />
      </div>

      <div class="backfill-runs-page__toolbar-actions">
        <label class="backfill-runs-page__filter">
          <span>Status:</span>
          <select
            :value="statusFilter"
            class="backfill-runs-page__filter-select"
            @change="emit('update:statusFilter', ($event.target as HTMLSelectElement).value)"
          >
            <option value="">All</option>
            <option v-for="option in statusOptions" :key="option" :value="option">{{ label(option) }}</option>
          </select>
        </label>

        <label class="backfill-runs-page__filter">
          <span>Trigger:</span>
          <select
            :value="triggerFilter"
            class="backfill-runs-page__filter-select"
            @change="emit('update:triggerFilter', ($event.target as HTMLSelectElement).value)"
          >
            <option value="">All</option>
            <option v-for="option in triggerOptions" :key="option" :value="option">{{ label(option) }}</option>
          </select>
        </label>

        <label class="backfill-runs-page__filter">
          <span>From:</span>
          <input
            :value="dateFromFilter"
            class="backfill-runs-page__filter-date"
            type="date"
            aria-label="Started from"
            @change="emit('update:dateFromFilter', ($event.target as HTMLInputElement).value)"
          />
        </label>

        <label class="backfill-runs-page__filter">
          <span>To:</span>
          <input
            :value="dateToFilter"
            class="backfill-runs-page__filter-date"
            type="date"
            aria-label="Started to"
            @change="emit('update:dateToFilter', ($event.target as HTMLInputElement).value)"
          />
        </label>

        <div class="backfill-runs-page__divider" />

        <button
          type="button"
          class="backfill-runs-page__btn-refresh"
          title="Refresh table"
          aria-label="Refresh table"
          @click="emit('refresh')"
        >
          Refresh
        </button>
      </div>
    </div>

    <AppDataTable ariaLabel="Backfill runs">
      <template #head>
        <tr>
          <th scope="col">Channel</th>
          <th scope="col">Page</th>
          <th scope="col">Trigger</th>
          <th scope="col">Status</th>
          <th scope="col">Started</th>
          <th scope="col">Finished</th>
          <th scope="col">Error</th>
          <th scope="col">Action</th>
        </tr>
      </template>

      <BackfillRunStateRow
        v-if="tableState.state !== 'ready'"
        :colspan="8"
        :message="tableState.message"
        :state="tableState.state"
        @retry="emit('refresh')"
      />

      <tr
        v-for="row in rows"
        v-else
        :key="row.id"
        class="backfill-runs-page__row"
        :class="{ 'backfill-runs-page__row--failed': row.statusTone === 'failed' }"
        @click="emit('openRow', row)"
      >
        <td class="backfill-runs-page__cell-channel">
          <button
            type="button"
            class="backfill-runs-page__channel-name"
            :aria-label="`Open backfill run details for ${row.channelName}`"
            @click.stop="emit('openRow', row)"
          >
            {{ row.channelName }}
          </button>
          <span class="backfill-runs-page__channel-id">{{ row.youtubeChannelId }}</span>
        </td>
        <td>{{ row.pageNumber }}</td>
        <td>{{ row.triggerLabel }}</td>
        <td>
          <span class="backfill-runs-page__status-pill" :class="`backfill-runs-page__status-pill--${row.statusTone}`">
            {{ row.statusLabel }}
          </span>
        </td>
        <td>{{ row.startedLabel }}</td>
        <td>{{ row.finishedLabel }}</td>
        <td class="backfill-runs-page__cell-error">{{ row.errorLabel }}</td>
        <td class="backfill-runs-page__cell-action">
          <button
            v-if="row.canRetry"
            type="button"
            class="backfill-runs-page__btn-retry"
            @click.stop="emit('retry', row)"
          >
            Retry backfill
          </button>
        </td>
      </tr>
    </AppDataTable>

    <div class="backfill-runs-page__pagination">
      <div class="backfill-runs-page__pagination-meta">
        <span class="backfill-runs-page__pagination-summary">{{ pagination.summary }}</span>
        <label class="backfill-runs-page__page-size" for="backfill-runs-items-per-page">
          <span>Items per page:</span>
          <select
            id="backfill-runs-items-per-page"
            :value="itemsPerPage"
            class="backfill-runs-page__page-size-select"
            @change="emit('update:itemsPerPage', ($event.target as HTMLSelectElement).value)"
          >
            <option v-for="option in pageSizeOptions" :key="option" :value="String(option)">{{ option }}</option>
          </select>
        </label>
      </div>
      <AppPagination
        :page="page"
        :page-count="pagination.pageCount"
        :display-pages="pagination.displayPages"
        @update:page="emit('update:page', $event)"
      />
    </div>
  </section>
</template>
