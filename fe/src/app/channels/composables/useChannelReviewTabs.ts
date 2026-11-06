import { computed, onMounted, ref } from 'vue'
import { getReviewCounts } from '@/app/channels/api/channels-api'
import type { ReviewCountsResponse } from '@/app/channels/api/types/review-counts'
import type { ChannelTab } from '@/app/channels/types/channel-review'

const counts = ref<ReviewCountsResponse | null>(null)

export async function loadReviewCounts(): Promise<void> {
  try {
    counts.value = await getReviewCounts()
  } catch {}
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
