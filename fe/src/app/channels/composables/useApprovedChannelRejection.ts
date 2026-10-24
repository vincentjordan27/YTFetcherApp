import { computed, ref } from 'vue'
import { rejectChannels } from '@/app/channels/api/channels-api'
import type { ApiValidationProblem } from '@/app/channels/api/types/api-error'
import type { ChannelDialogTarget, ChannelReviewRow } from '@/app/channels/types/channel-review'
import { ApiError } from '@/common/api/api-client'
import { useToast } from '@/common/composables/useToast'

const REJECTION_REASON_MAX_LENGTH = 500

const MUTATION_MISSING_MESSAGE = 'This channel no longer exists. Refresh the list and try again.'
const MUTATION_CONFLICT_MESSAGE =
  'This channel was already reviewed or is no longer approved. Refresh the list and try again.'
const MUTATION_FALLBACK_MESSAGE = "Couldn't save the change. Check your connection and try again."
const SUCCESS_MESSAGE = 'Channel rejected. Backfill has stopped.'

/**
 * Dialog state and the rejection lifecycle for an approved channel. It owns the reason, the in-flight
 * guard, the API-error mapping, and the success toast; the page supplies the reload callback.
 */
export function useApprovedChannelRejection(onSettled: () => Promise<void>) {
  const selectedChannel = ref<ChannelDialogTarget | null>(null)
  const rejectReason = ref('')
  const isMutating = ref(false)
  const error = ref<string | null>(null)
  const selectedIds = ref<ReadonlySet<string>>(new Set())
  const { showToast } = useToast()

  const activeDialog = computed(() => (selectedChannel.value === null ? null : ('reject' as const)))

  const canSubmit = computed(() => {
    const reason = rejectReason.value.trim()
    return reason.length > 0 && reason.length <= REJECTION_REASON_MAX_LENGTH
  })
  const selectedCount = computed(() => selectedIds.value.size)
  const selectedIdList = computed(() => [...selectedIds.value])

  function open(channel: ChannelReviewRow): void {
    selectedIds.value = new Set([channel.id])
    openSelected([channel])
  }

  function openSelected(rows: readonly ChannelReviewRow[]): void {
    const channel = rows.find(item => selectedIds.value.has(item.id))
    if (channel === undefined || isMutating.value) return

    selectedChannel.value = { id: channel.id, name: channel.name, handle: channel.handle }
    rejectReason.value = ''
    error.value = null
  }

  function close(): void {
    if (isMutating.value) return
    selectedChannel.value = null
    error.value = null
  }

  function updateReason(value: string): void {
    rejectReason.value = value
  }

  async function confirm(): Promise<void> {
    const channel = selectedChannel.value
    if (channel === null || isMutating.value || !canSubmit.value || selectedIdList.value.length === 0) return

    isMutating.value = true
    error.value = null
    const submittedCount = selectedIdList.value.length

    try {
      await rejectChannels(selectedIdList.value, rejectReason.value.trim())
    } catch (caught) {
      // Keep the dialog open with the readable server message, but always refresh durable state.
      error.value = rejectionErrorMessage(caught)
      isMutating.value = false
      await onSettled()
      return
    }

    isMutating.value = false
    selectedChannel.value = null
    rejectReason.value = ''
    await onSettled()
    showToast({
      tone: 'success',
      message: submittedCount === 1 ? SUCCESS_MESSAGE : `${submittedCount} channels rejected. Backfill has stopped.`,
    })
  }

  function toggleRowSelection(channelId: string): void {
    const next = new Set(selectedIds.value)
    if (next.has(channelId)) next.delete(channelId)
    else next.add(channelId)
    selectedIds.value = next
  }

  function toggleVisibleSelection(rows: readonly ChannelReviewRow[]): void {
    const visibleIds = rows.map(channel => channel.id)
    const allSelected = visibleIds.length > 0 && visibleIds.every(id => selectedIds.value.has(id))
    const next = new Set(selectedIds.value)

    for (const id of visibleIds) {
      if (allSelected) next.delete(id)
      else next.add(id)
    }

    selectedIds.value = next
  }

  function clearSelection(): void {
    selectedIds.value = new Set()
  }

  return {
    activeDialog,
    selectedChannel,
    rejectReason,
    isMutating,
    error,
    canSubmit,
    selectedIdList,
    selectedCount,
    open,
    openSelected,
    close,
    updateReason,
    confirm,
    toggleRowSelection,
    toggleVisibleSelection,
    clearSelection,
  }
}

function rejectionErrorMessage(caught: unknown): string {
  if (caught instanceof ApiError) {
    if (caught.status === 404) {
      return MUTATION_MISSING_MESSAGE
    }

    if (caught.status === 409) {
      return MUTATION_CONFLICT_MESSAGE
    }

    if (caught.status === 400 && typeof caught.body === 'object' && caught.body !== null) {
      const messages = (caught.body as ApiValidationProblem).errors?.reason
      if (messages && messages.length > 0) {
        return messages[0]
      }
    }
  }

  return MUTATION_FALLBACK_MESSAGE
}
