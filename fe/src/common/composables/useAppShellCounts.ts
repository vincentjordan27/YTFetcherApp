import { onMounted, ref, watch } from 'vue'
import { useRoute } from 'vue-router'
import { getReviewCounts } from '@/app/channels/api/channels-api'
import { getActiveVideoCount } from '@/app/videos/api/videos-api'
import { formatCompactCount } from '@/common/data/format-compact-count'

/**
 * The two live sidebar badges. Both counts load on shell mount and on route navigation. A count whose
 * request failed is hidden rather than rendered as a fabricated zero or a stale template number, and a
 * failure of one count never blocks the other or the navigation itself.
 */
export function useAppShellCounts() {
  const route = useRoute()
  const videosCount = ref<number | null>(null)
  const channelsCount = ref<number | null>(null)

  async function load(): Promise<void> {
    const [videos, channels] = await Promise.allSettled([getActiveVideoCount(), getReviewCounts()])

    videosCount.value = videos.status === 'fulfilled' ? videos.value.count : null
    channelsCount.value = channels.status === 'fulfilled' ? channels.value.pending : null
  }

  function badgeFor(itemId: string): string | undefined {
    if (itemId === 'videos') {
      return videosCount.value === null ? undefined : formatCompactCount(videosCount.value)
    }

    if (itemId === 'channels') {
      return channelsCount.value === null ? undefined : formatCompactCount(channelsCount.value)
    }

    // No other sidebar badge exists.
    return undefined
  }

  onMounted(() => {
    void load()
  })

  watch(
    () => route.fullPath,
    () => {
      void load()
    },
  )

  return { badgeFor }
}
