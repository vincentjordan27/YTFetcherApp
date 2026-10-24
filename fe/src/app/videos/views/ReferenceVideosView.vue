<script setup lang="ts">
import AdminShell from '@/common/layouts/AdminShell.vue'
import type { AppTabItem } from '@/common/components/AppTabs.vue'
import ReferenceVideoTable from '@/app/videos/components/ReferenceVideoTable.vue'
import ShortsPreviewDialog from '@/app/videos/components/ShortsPreviewDialog.vue'
import VideoHistoryDialog from '@/app/videos/components/VideoHistoryDialog.vue'
import { useReferenceVideos } from '@/app/videos/composables/useReferenceVideos'
import { useShortsPreview } from '@/app/videos/composables/useShortsPreview'
import { useVideoHistory } from '@/app/videos/composables/useVideoHistory'
import type { ReferenceVideoRow } from '@/app/videos/types/reference-video.types'

/**
 * Live Reference tab. Rows come from `POST /api/videos/reference/list`; the per-row remove-reference action
 * refreshes server truth instead of splicing the rendered list. Sorting is server-side.
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
  updateSearchInput,
  goToPage,
  updateRowsPerPage,
  setSort,
  retry,
  removeReference,
  removePending,
} = useReferenceVideos()

// Navigation only: the tab counts have no endpoint yet, so no badge is fabricated.
const tabs = [
  { id: 'active', label: 'Active', to: { name: 'videos-active' }, tone: 'active' },
  { id: 'reference', label: 'Reference', to: { name: 'videos-reference' }, tone: 'info' },
] as const satisfies readonly AppTabItem[]

// History is loaded only after the operator opens it from a row's three-dot menu.
const history = useVideoHistory()

function openHistory(row: ReferenceVideoRow): void {
  history.openFor({ videoId: row.id, title: row.title, channelName: row.channelName })
}

// The watch popup plays the provider id the row already carries and makes no API request.
const preview = useShortsPreview()

function openPreview(row: ReferenceVideoRow): void {
  preview.openFor({
    youtubeVideoId: row.youtubeVideoId,
    title: row.title,
    channelName: row.channelName,
  })
}
</script>

<template>
  <AdminShell>
    <ReferenceVideoTable
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
      :remove-pending="removePending"
      @update:search-query="updateSearchInput"
      @update:rows-per-page="updateRowsPerPage"
      @update:page="goToPage"
      @set-sort="setSort"
      @retry="retry"
      @open-history="openHistory"
      @open-preview="openPreview"
      @remove-reference="removeReference"
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
