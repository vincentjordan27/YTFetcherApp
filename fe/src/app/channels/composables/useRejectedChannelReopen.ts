import { ref } from 'vue'
import { reopenChannel } from '@/app/channels/api/channels-api'
import type { ChannelReviewRow } from '@/app/channels/types/channel-review'
import { ApiError } from '@/common/api/api-client'
import { useToast } from '@/common/composables/useToast'

const MISSING_MESSAGE = 'This channel no longer exists. Refresh the list and try again.'
const RUNNING_MESSAGE = 'The current backfill page is still running. Wait for it to finish, then try again.'
const CONFLICT_MESSAGE = 'This channel is no longer rejected. Refresh the list and try again.'
const FALLBACK_MESSAGE = "Couldn't reopen the channel. Check your connection and try again."
const SUCCESS_MESSAGE = 'Channel returned to pending review.'

/** Reopens one rejected channel without starting another quota-consuming backfill. */
export function useRejectedChannelReopen(onSettled: () => Promise<void>) {
  const reopeningChannelId = ref<string | null>(null)
  const { showToast } = useToast()

  async function reopen(channel: ChannelReviewRow): Promise<void> {
    if (reopeningChannelId.value !== null) return

    reopeningChannelId.value = channel.id

    try {
      await reopenChannel(channel.id)
      await onSettled()
      showToast({ tone: 'success', message: SUCCESS_MESSAGE })
    } catch (caught) {
      await onSettled()
      showToast({ tone: 'failure', message: reopenErrorMessage(caught) })
    } finally {
      reopeningChannelId.value = null
    }
  }

  return { reopeningChannelId, reopen }
}

function reopenErrorMessage(caught: unknown): string {
  if (caught instanceof ApiError) {
    if (caught.status === 404) return MISSING_MESSAGE

    if (caught.status === 409) {
      const currentBackfillStatus = readCurrentBackfillStatus(caught.body)
      if (currentBackfillStatus === 'RUNNING') return RUNNING_MESSAGE
      return CONFLICT_MESSAGE
    }
  }

  return FALLBACK_MESSAGE
}

function readCurrentBackfillStatus(body: unknown): string | undefined {
  if (typeof body !== 'object' || body === null || !('currentBackfillStatus' in body)) return undefined

  const value = body.currentBackfillStatus
  return typeof value === 'string' ? value : undefined
}
