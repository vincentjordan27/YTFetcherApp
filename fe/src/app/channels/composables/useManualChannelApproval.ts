import { computed, ref } from 'vue'
import { manualApproveChannel } from '@/app/channels/api/channels-api'
import type { ApiValidationProblem } from '@/app/channels/api/types/api-error'
import { ApiError } from '@/common/api/api-client'
import { useToast } from '@/common/composables/useToast'

const ADDED_MESSAGE = 'Channel added and approved. Backfill will start automatically.'
const SCHEDULING_FAILURE_MESSAGE = 'Channel added, but backfill scheduling needs retry.'
const UNEXPECTED_FAILURE_MESSAGE = "Couldn't add the channel. Check your connection and try again."

/**
 * Dialog state and the single submit lifecycle for adding one channel by `@handle`. It owns the
 * field value, the in-flight guard, the API-error mapping, and the success toast. The page supplies
 * the reload callback that refreshes the pending list and the shared review tab counts.
 */
export function useManualChannelApproval(onAdded: () => Promise<void>) {
  const isOpen = ref(false)
  const channelReference = ref('')
  const pending = ref(false)
  const error = ref<string | null>(null)
  const { showToast } = useToast()

  const canSubmit = computed(() => channelReference.value.trim().length > 0 && !pending.value)

  function open(): void {
    channelReference.value = ''
    error.value = null
    isOpen.value = true
  }

  function close(): void {
    if (pending.value) {
      return
    }

    isOpen.value = false
    error.value = null
  }

  function updateChannelReference(value: string): void {
    channelReference.value = value
  }

  async function submit(): Promise<void> {
    if (!canSubmit.value) {
      return
    }

    pending.value = true
    error.value = null

    let backfillStatus: string
    try {
      const detail = await manualApproveChannel(channelReference.value.trim())
      backfillStatus = detail.backfillStatus
    } catch (caught) {
      // Keep the dialog open so the readable server message stays visible.
      error.value = manualAddErrorMessage(caught)
      pending.value = false
      return
    }

    pending.value = false
    isOpen.value = false
    channelReference.value = ''
    await onAdded()
    showToast({
      tone: 'success',
      message: backfillStatus === 'SCHEDULING_FAILED' ? SCHEDULING_FAILURE_MESSAGE : ADDED_MESSAGE,
    })
  }

  return {
    isOpen,
    channelReference,
    pending,
    error,
    canSubmit,
    open,
    close,
    updateChannelReference,
    submit,
  }
}

/**
 * Surfaces the server's readable message for a `400`, `404`, or `409`, and the established generic
 * fallback for anything unexpected. Provider detail is never shown.
 */
function manualAddErrorMessage(caught: unknown): string {
  if (caught instanceof ApiError && typeof caught.body === 'object' && caught.body !== null) {
    const problem = caught.body as ApiValidationProblem
    const fieldMessages = problem.errors?.channelReference
    if (fieldMessages && fieldMessages.length > 0) {
      return fieldMessages[0]
    }

    if (typeof problem.detail === 'string' && problem.detail.length > 0) {
      return problem.detail
    }
  }

  return UNEXPECTED_FAILURE_MESSAGE
}
