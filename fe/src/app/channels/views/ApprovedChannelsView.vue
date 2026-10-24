<script setup lang="ts">
import { ref } from 'vue'
import AdminShell from '@/common/layouts/AdminShell.vue'
import ChannelActionDialog from '@/app/channels/components/ChannelActionDialog.vue'
import ChannelBackfillFailureDialog from '@/app/channels/components/ChannelBackfillFailureDialog.vue'
import ChannelReviewPagination from '@/app/channels/components/ChannelReviewPagination.vue'
import ChannelReviewPageFrame from '@/app/channels/components/ChannelReviewPageFrame.vue'
import ChannelReviewTable from '@/app/channels/components/ChannelReviewTable.vue'
import ChannelReviewToolbar from '@/app/channels/components/ChannelReviewToolbar.vue'
import { useApprovedChannelRejection } from '@/app/channels/composables/useApprovedChannelRejection'
import { useChannelLedger } from '@/app/channels/composables/useChannelLedger'
import { loadReviewCounts } from '@/app/channels/composables/useChannelReviewTabs'
import type { ChannelDialogKind, ChannelReviewRow } from '@/app/channels/types/channel-review'

const {
  tabs,
  searchInput,
  currentPage,
  rowsPerPage,
  rows,
  pagination,
  tableState,
  updateSearchInput,
  goToPage,
  updateRowsPerPage,
  retry,
  reload,
} = useChannelLedger('approved')

// Approved-channel rejection: the dialog reloads the ledger and the shared tab counts on settle.
async function refreshLedger(): Promise<void> {
  await reload()
  await loadReviewCounts()
  clearSelection()
}

const {
  activeDialog,
  selectedChannel,
  rejectReason,
  isMutating,
  error: rejectError,
  selectedIdList,
  selectedCount,
  open: openRejectDialog,
  openSelected: openSelectedRejectDialog,
  close: closeRejectDialog,
  updateReason: updateRejectReason,
  confirm: confirmReject,
  toggleRowSelection,
  toggleVisibleSelection,
  clearSelection,
} = useApprovedChannelRejection(refreshLedger)

function handleOpenAction(kind: ChannelDialogKind, channel: ChannelReviewRow): void {
  if (kind === 'reject') {
    openRejectDialog(channel)
  }
}

// The only interactive element of the backfill status cell: a read-only detail dialog. Retrying failed
// page work moved to the Backfill runs page, which owns the terminal-failure retry.
const failedChannel = ref<ChannelReviewRow | null>(null)

function openFailureDialog(channel: ChannelReviewRow): void {
  failedChannel.value = channel
}

function closeFailureDialog(): void {
  failedChannel.value = null
}
</script>

<template>
  <AdminShell>
    <ChannelReviewPageFrame title="Approved channels" status="approved">
      <ChannelReviewToolbar
        :tabs="tabs"
        :search-query="searchInput"
        search-label="Filter by name or channel ID"
        :selected-count="selectedCount"
        show-reject-selection
        @update:search-query="updateSearchInput"
        @reject-selection="openSelectedRejectDialog(rows)"
      />
      <div class="approved-channels__ledger">
        <ChannelReviewTable
          variant="approved"
          :rows="rows"
          :state="tableState.state"
          :state-message="tableState.message"
          :selected-ids="selectedIdList"
          selection-enabled
          @retry="retry"
          @toggle-selection="toggleRowSelection"
          @toggle-visible-selection="toggleVisibleSelection(rows)"
          @open-action="handleOpenAction"
          @open-backfill-failure="openFailureDialog"
        />
        <ChannelReviewPagination
          :pagination="pagination"
          :page="currentPage"
          :rows-per-page="String(rowsPerPage)"
          @update:page="goToPage"
          @update:rows-per-page="updateRowsPerPage"
        />
      </div>
      <ChannelBackfillFailureDialog
        :channel="failedChannel"
        @close="closeFailureDialog"
      />
    </ChannelReviewPageFrame>
    <ChannelActionDialog
      :kind="activeDialog"
      :channel="selectedChannel"
      :selection-count="selectedCount"
      :rejection-reason="rejectReason"
      :pending="isMutating"
      :error="rejectError"
      rejection-context="approved"
      @close="closeRejectDialog"
      @confirm="confirmReject"
      @update:rejection-reason="updateRejectReason"
    />
  </AdminShell>
</template>

<style lang="scss" scoped>
@use '@/app/channels/styles/channel-review-ledger';
</style>
