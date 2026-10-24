import { computed, onMounted, ref } from 'vue'
import { getReviewCounts } from '@/app/channels/api/channels-api'
import type { ReviewCountsResponse } from '@/app/channels/api/types/review-counts'
import type { ChannelTab } from '@/app/channels/types/channel-review'

/**
 * Counts are shared across the three channel ledgers so every visible tab badge stays real while
 * another tab is open. While the counts have never been loaded successfully the badges are omitted
 * instead of rendered as 0; a later failed refresh keeps the last known values.
 */
const counts = ref<ReviewCountsResponse | null>(null)

export async function loadReviewCounts(): Promise<void> {
  try {
    counts.value = await getReviewCounts()
  } catch {
    // Keep the previous counts. The ledger's list request reports failures to the operator.
  }
}

export function useChannelReviewTabs() {
  const tabs = computed<readonly ChannelTab[]>(() => {
    const loaded = counts.value

    return [
      {
        id: 'pending',
        label: 'Pending',
        badge: loaded === null ? undefined : String(loaded.pending),
        to: { name: 'channels-pending' },
        tone: 'warning',
      },
      {
        id: 'approved',
        label: 'Approved',
        badge: loaded === null ? undefined : String(loaded.approved),
        to: { name: 'channels-approved' },
        tone: 'active',
      },
      {
        id: 'rejected',
        label: 'Rejected',
        badge: loaded === null ? undefined : String(loaded.rejected),
        to: { name: 'channels-rejected' },
        tone: 'danger',
      },
    ]
  })

  onMounted(() => {
    void loadReviewCounts()
  })

  return { tabs, loadReviewCounts }
}
