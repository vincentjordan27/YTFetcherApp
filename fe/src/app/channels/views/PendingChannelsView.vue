<script setup lang="ts">
import AdminShell from '@/common/layouts/AdminShell.vue'
import ChannelActionDialog from '@/app/channels/components/ChannelActionDialog.vue'
import ChannelReviewTable from '@/app/channels/components/ChannelReviewTable.vue'
import ChannelReviewPageFrame from '@/app/channels/components/ChannelReviewPageFrame.vue'
import ChannelReviewPagination from '@/app/channels/components/ChannelReviewPagination.vue'
import ChannelReviewToolbar from '@/app/channels/components/ChannelReviewToolbar.vue'
import ManualChannelDialog from '@/app/channels/components/ManualChannelDialog.vue'
import { useManualChannelApproval } from '@/app/channels/composables/useManualChannelApproval'
import { usePendingChannelReview } from '@/app/channels/composables/usePendingChannelReview'

const {
  searchInput,
  rowsPerPage,
  currentPage,
  rows,
  tabs,
  pagination,
  tableState,
  evidenceState,
  expandedEvidenceId,
  activeDialog,
  selectedChannel,
  rejectReason,
  isMutating,
  mutationError,
  selectedIdList,
  selectedCount,
  updateSearchInput,
  goToPage,
  updateRowsPerPage,
  retry,
  refresh,
  toggleEvidence,
  openActionDialog,
  openSelectedAction,
  closeActionDialog,
  updateRejectReason,
  confirmAction,
  toggleRowSelection,
  toggleVisibleSelection,
} = usePendingChannelReview()

// The manual-add dialog reloads the pending page and the shared tab counts on success.
const {
  isOpen: isManualDialogOpen,
  channelReference: manualChannelReference,
  pending: isManualPending,
  error: manualError,
  open: openManualDialog,
  close: closeManualDialog,
  updateChannelReference: updateManualChannelReference,
  submit: submitManualChannel,
} = useManualChannelApproval(refresh)
</script>

<template>
  <AdminShell>
    <ChannelReviewPageFrame title="Pending channels" status="pending">
      <ChannelReviewToolbar
        :tabs="tabs"
        :search-query="searchInput"
        search-label="Filter by name or channel ID"
        show-add-channel
        :selected-count="selectedCount"
        show-approve-selection
        show-reject-selection
        @update:search-query="updateSearchInput"
        @add-channel="openManualDialog"
        @approve-selection="openSelectedAction('approve')"
        @reject-selection="openSelectedAction('reject')"
      />
      <div class="pending-channels__ledger">
        <ChannelReviewTable
          variant="pending"
          :rows="rows"
          :expanded-evidence-id="expandedEvidenceId"
          :state="tableState.state"
          :state-message="tableState.message"
          :evidence="evidenceState"
          :selected-ids="selectedIdList"
          selection-enabled
          @toggle-evidence="toggleEvidence"
          @toggle-selection="toggleRowSelection"
          @toggle-visible-selection="toggleVisibleSelection"
          @open-action="openActionDialog"
          @retry="retry"
        />
        <ChannelReviewPagination
          :pagination="pagination"
          :page="currentPage"
          :rows-per-page="String(rowsPerPage)"
          @update:page="goToPage"
          @update:rows-per-page="updateRowsPerPage"
        />
      </div>
    </ChannelReviewPageFrame>
    <ChannelActionDialog
      :kind="activeDialog"
      :channel="selectedChannel"
      :selection-count="selectedCount"
      :rejection-reason="rejectReason"
      :pending="isMutating"
      :error="mutationError"
      @close="closeActionDialog"
      @confirm="confirmAction"
      @update:rejection-reason="updateRejectReason"
    />
    <ManualChannelDialog
      :open="isManualDialogOpen"
      :channel-reference="manualChannelReference"
      :pending="isManualPending"
      :error="manualError"
      @close="closeManualDialog"
      @submit="submitManualChannel"
      @update:channel-reference="updateManualChannelReference"
    />
  </AdminShell>
</template>

<style lang="scss" scoped>
@use '@/app/channels/styles/channel-review-ledger';
</style>
