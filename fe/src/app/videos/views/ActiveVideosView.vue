<script setup lang="ts">
import AdminShell from '@/common/layouts/AdminShell.vue'
import type { AppTabItem } from '@/common/components/AppTabs.vue'
import ActiveVideoTable from '@/app/videos/components/ActiveVideoTable.vue'
import MarkReferenceDialog from '@/app/videos/components/MarkReferenceDialog.vue'
import ShortsPreviewDialog from '@/app/videos/components/ShortsPreviewDialog.vue'
import VideoHistoryDialog from '@/app/videos/components/VideoHistoryDialog.vue'
import { useActiveVideos } from '@/app/videos/composables/useActiveVideos'
import { useShortsPreview } from '@/app/videos/composables/useShortsPreview'
import { useVideoHistory } from '@/app/videos/composables/useVideoHistory'
import type { ActiveVideoRow } from '@/app/videos/types/active-video.types'

/**
 * Live Active tab. Rows come from `POST /api/videos/active/list`; the selection is page-local and the
 * mark-as-reference command is the only mutation on this route. Sorting is server-side.
 */
const {
  searchInput,
  currentPage,
  rowsPerPage,
  sort,
  sortLabel,
  rows,
  pagination,
  tableState,
  selectedIdList,
  selectedCount,
  canMarkReference,
  markReferenceOpen,
  markReferencePending,
  markReferenceError,
  updateSearchInput,
  goToPage,
  updateRowsPerPage,
  setSort,
  retry,
  toggleRowSelection,
  toggleVisibleSelection,
  openMarkReference,
  openSingleMarkReference,
  closeMarkReference,
  confirmMarkReference,
} = useActiveVideos()

// Navigation only: the tab counts have no endpoint yet, so no badge is fabricated.
const tabs = [
  { id: 'active', label: 'Active', to: { name: 'videos-active' }, tone: 'active' },
  { id: 'reference', label: 'Reference', to: { name: 'videos-reference' }, tone: 'info' },
] as const satisfies readonly AppTabItem[]

// History is loaded only after the operator opens it from a row's three-dot menu.
const history = useVideoHistory()

function openHistory(row: ActiveVideoRow): void {
  history.openFor({ videoId: row.id, title: row.title, channelName: row.channelName })
}

// The watch popup plays the provider id the row already carries and makes no API request.
const preview = useShortsPreview()

function openPreview(row: ActiveVideoRow): void {
  preview.openFor({
    youtubeVideoId: row.youtubeVideoId,
    title: row.title,
    channelName: row.channelName,
  })
}
</script>

<template>
  <AdminShell>
    <ActiveVideoTable
      :tabs="tabs"
      :search-query="searchInput"
      :rows-per-page="rowsPerPage"
      :current-page="currentPage"
      :pagination="pagination"
      :rows="rows"
      :state="tableState.state"
      :state-message="tableState.message"
      :sort="sort"
      :sort-label="sortLabel"
      :selected-ids="selectedIdList"
      :selected-count="selectedCount"
      :can-mark-reference="canMarkReference"
      @update:search-query="updateSearchInput"
      @update:rows-per-page="updateRowsPerPage"
      @update:page="goToPage"
      @set-sort="setSort"
      @retry="retry"
      @toggle-selection="toggleRowSelection"
      @toggle-visible-selection="toggleVisibleSelection"
      @open-mark-reference="openMarkReference"
      @open-mark-single-reference="openSingleMarkReference($event.id)"
      @open-history="openHistory"
      @open-preview="openPreview"
    />
    <MarkReferenceDialog
      :open="markReferenceOpen"
      :selected-count="selectedCount"
      :pending="markReferencePending"
      :error="markReferenceError"
      @close="closeMarkReference"
      @confirm="confirmMarkReference"
    />
    <VideoHistoryDialog
      :open="history.open.value"
      :target="history.target.value"
      :rows="history.rows.value"
      :pagination="history.pagination.value"
      :current-page="history.currentPage.value"
      :state="history.state.value"
      :state-message="history.stateMessage.value"
      @close="history.close"
      @retry="history.retry"
      @update:page="history.goToPage"
    />
    <ShortsPreviewDialog
      :open="preview.open.value"
      :target="preview.target.value"
      @close="preview.close"
    />
  </AdminShell>
</template>
