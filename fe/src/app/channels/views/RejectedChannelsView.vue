<script setup lang="ts">
import AdminShell from '@/common/layouts/AdminShell.vue'
import ChannelReviewPagination from '@/app/channels/components/ChannelReviewPagination.vue'
import ChannelReviewPageFrame from '@/app/channels/components/ChannelReviewPageFrame.vue'
import ChannelReviewTable from '@/app/channels/components/ChannelReviewTable.vue'
import ChannelReviewToolbar from '@/app/channels/components/ChannelReviewToolbar.vue'
import ChannelRejectionReasonDialog from '@/app/channels/components/ChannelRejectionReasonDialog.vue'
import { useChannelLedger } from '@/app/channels/composables/useChannelLedger'
import { useRejectedChannelReopen } from '@/app/channels/composables/useRejectedChannelReopen'
import type { ChannelReviewRow } from '@/app/channels/types/channel-review'
import { ref } from 'vue'

const selectedChannel = ref<ChannelReviewRow | null>(null)

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
} = useChannelLedger('rejected')

const { reopeningChannelId, reopen } = useRejectedChannelReopen(reload)
</script>

<template>
  <AdminShell>
    <ChannelReviewPageFrame title="Rejected channels" status="rejected">
      <ChannelReviewToolbar
        :tabs="tabs"
        :search-query="searchInput"
        search-label="Filter by name or channel ID"
        @update:search-query="updateSearchInput"
      />
      <div class="rejected-channels__ledger">
        <ChannelReviewTable
          variant="rejected"
          :rows="rows"
          :state="tableState.state"
          :state-message="tableState.message"
          :reopening-channel-id="reopeningChannelId"
          @retry="retry"
          @reopen="reopen"
          @open-rejection-reason="selectedChannel = $event"
        />
        <ChannelReviewPagination
          :pagination="pagination"
          :page="currentPage"
          :rows-per-page="String(rowsPerPage)"
          @update:page="goToPage"
          @update:rows-per-page="updateRowsPerPage"
        />
      </div>
      <ChannelRejectionReasonDialog :channel="selectedChannel" @close="selectedChannel = null" />
    </ChannelReviewPageFrame>
  </AdminShell>
</template>

<style lang="scss" scoped>
@use '@/app/channels/styles/channel-review-ledger';
</style>
