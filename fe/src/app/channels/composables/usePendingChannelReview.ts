import { computed, onBeforeUnmount, onMounted, ref } from 'vue'
import { approveChannels, getChannelEvidence, getPendingChannels, rejectChannels } from '@/app/channels/api/channels-api'
import type { ApiValidationProblem } from '@/app/channels/api/types/api-error'
import { toChannelEvidenceView, toPendingChannelRow } from '@/app/channels/data/channel-review.mapper'
import {
  CHANNEL_INITIAL_PAGE,
  CHANNEL_SEARCH_DEBOUNCE_MS,
  buildChannelPagination,
} from '@/app/channels/data/channel-pagination'
import { useChannelReviewTabs } from '@/app/channels/composables/useChannelReviewTabs'
import type {
  ChannelDialogKind,
  ChannelDialogTarget,
  ChannelEvidenceState,
  ChannelEvidenceView,
  ChannelPaginationDesign,
  ChannelReviewRow,
} from '@/app/channels/types/channel-review'
import { ApiError } from '@/common/api/api-client'
import { useToast } from '@/common/composables/useToast'
import { getInitialTablePageSize } from '@/common/data/initial-table-page-size'

const REJECTION_REASON_MAX_LENGTH = 500

const MUTATION_CONFLICT_MESSAGE =
  'This channel was already reviewed or is no longer pending. Refresh the list and try again.'
const MUTATION_MISSING_MESSAGE = 'This channel no longer exists. Refresh the list and try again.'
const MUTATION_FALLBACK_MESSAGE = "Couldn't save the change. Check your connection and try again."

const PENDING_LIST_LOADING_MESSAGE = 'Loading pending channels…'
const PENDING_LIST_EMPTY_MESSAGE = 'No pending channels match this filter.'
const PENDING_LIST_FAILURE_MESSAGE = "Couldn't load pending channels."

export function usePendingChannelReview() {
  const searchInput = ref('')
  const searchText = ref('')
  const rowsPerPage = ref(getInitialTablePageSize())
  const currentPage = ref(CHANNEL_INITIAL_PAGE)
  const rows = ref<ChannelReviewRow[]>([])
  const totalCount = ref(0)
  const loading = ref(false)
  const requestFailed = ref(false)
  const expandedEvidenceId = ref<string | null>(null)
  const evidenceItems = ref<Record<string, ChannelEvidenceView[]>>({})
  const evidenceFailed = ref<Record<string, boolean>>({})
  const evidenceLoadingId = ref<string | null>(null)
  const activeDialog = ref<ChannelDialogKind | null>(null)
  const selectedChannel = ref<ChannelDialogTarget | null>(null)
  const rejectReason = ref('')
  const isMutating = ref(false)
  const mutationError = ref<string | null>(null)
  const selectedIds = ref<ReadonlySet<string>>(new Set())
  const { showToast } = useToast()
  const { tabs, loadReviewCounts } = useChannelReviewTabs()

  let pendingListRequestId = 0
  // Lifecycle epoch for the evidence path. It is captured by every evidence request and bumped only on unmount,
  // so an evidence response can never publish into a feature that is already gone while two channels' requests
  // stay independent of each other.
  let evidenceLifecycleId = 0
  let searchTimer: ReturnType<typeof window.setTimeout> | undefined

  const pagination = computed<ChannelPaginationDesign>(() =>
    buildChannelPagination(currentPage.value, rowsPerPage.value, totalCount.value),
  )

  const tableState = computed(() => {
    if (loading.value) {
      return { state: 'loading' as const, message: PENDING_LIST_LOADING_MESSAGE }
    }

    if (requestFailed.value) {
      return { state: 'error' as const, message: PENDING_LIST_FAILURE_MESSAGE }
    }

    if (rows.value.length === 0) {
      return { state: 'empty' as const, message: PENDING_LIST_EMPTY_MESSAGE }
    }

    return { state: 'ready' as const, message: '' }
  })

  const evidenceState = computed<ChannelEvidenceState>(() => ({
    items: evidenceItems.value,
    loadingId: evidenceLoadingId.value,
    failed: evidenceFailed.value,
  }))

  const canSubmitRejection = computed(() => {
    const reason = rejectReason.value.trim()
    return reason.length > 0 && reason.length <= REJECTION_REASON_MAX_LENGTH
  })
  const selectedCount = computed(() => selectedIds.value.size)
  const selectedIdList = computed(() => [...selectedIds.value])

  onMounted(() => {
    void loadPending()
  })

  // Unmount invalidates the debounce, the in-flight list request, and every in-flight evidence request, so no
  // late response can write list or evidence state after the feature is gone.
  onBeforeUnmount(() => {
    window.clearTimeout(searchTimer)
    searchTimer = undefined
    pendingListRequestId += 1
    evidenceLifecycleId += 1
  })

  async function loadPending(): Promise<void> {
    const requestId = ++pendingListRequestId
    loading.value = true
    requestFailed.value = false

    try {
      const response = await getPendingChannels({
        page: currentPage.value,
        pageSize: rowsPerPage.value,
        query: searchText.value || undefined,
      })

      if (requestId !== pendingListRequestId) {
        return
      }

      const lastValidPage = Math.max(CHANNEL_INITIAL_PAGE, Math.ceil(response.totalCount / rowsPerPage.value))
      if (currentPage.value > lastValidPage) {
        // Exactly one replacement request so an invalid range is never rendered.
        currentPage.value = lastValidPage
        loading.value = false
        await loadPending()
        return
      }

      rows.value = response.items.map(toPendingChannelRow)
      totalCount.value = response.totalCount
      clearSelection()
    } catch {
      if (requestId !== pendingListRequestId) {
        return
      }

      rows.value = []
      totalCount.value = 0
      requestFailed.value = true
    } finally {
      if (requestId === pendingListRequestId) {
        loading.value = false
      }
    }
  }

  /** Debounces the search box, applies it as the server-side `query`, and restarts at page one. */
  function updateSearchInput(value: string): void {
    searchInput.value = value
    window.clearTimeout(searchTimer)

    searchTimer = window.setTimeout(() => {
      const nextSearchText = value.trim()
      if (nextSearchText === searchText.value) {
        return
      }

      searchText.value = nextSearchText
      currentPage.value = CHANNEL_INITIAL_PAGE
      void loadPending()
    }, CHANNEL_SEARCH_DEBOUNCE_MS)
  }

  function goToPage(page: number): void {
    if (page === currentPage.value || page < 1) {
      return
    }

    currentPage.value = page
    void loadPending()
  }

  function updateRowsPerPage(value: string): void {
    const pageSize = Number(value)
    if (!Number.isFinite(pageSize) || pageSize < 1 || pageSize === rowsPerPage.value) {
      return
    }

    rowsPerPage.value = pageSize
    currentPage.value = CHANNEL_INITIAL_PAGE
    void loadPending()
  }

  function retry(): void {
    void loadPending()
  }

  /** Server truth after a mutation: reload the pending page and the shared tab counts together. */
  async function refresh(): Promise<void> {
    await loadPending()
    await loadReviewCounts()
  }

  function toggleEvidence(channelId: string): void {
    if (expandedEvidenceId.value === channelId) {
      expandedEvidenceId.value = null
      return
    }

    expandedEvidenceId.value = channelId

    if (evidenceItems.value[channelId] || evidenceLoadingId.value === channelId) {
      return
    }

    void loadEvidence(channelId)
  }

  async function loadEvidence(channelId: string): Promise<void> {
    const lifecycleId = evidenceLifecycleId
    evidenceLoadingId.value = channelId
    updateEvidenceFailure(channelId, false)

    try {
      const response = await getChannelEvidence(channelId)

      // The captured epoch is the only ownership test: a response that outlives its feature writes nothing,
      // while another channel's newer request never invalidates this one.
      if (lifecycleId !== evidenceLifecycleId) {
        return
      }

      evidenceItems.value = {
        ...evidenceItems.value,
        [channelId]: response.items.map(toChannelEvidenceView),
      }
    } catch {
      if (lifecycleId !== evidenceLifecycleId) {
        return
      }

      updateEvidenceFailure(channelId, true)
    } finally {
      if (lifecycleId === evidenceLifecycleId && evidenceLoadingId.value === channelId) {
        evidenceLoadingId.value = null
      }
    }
  }

  function openActionDialog(kind: ChannelDialogKind, channel: ChannelReviewRow): void {
    selectedIds.value = new Set([channel.id])
    openSelectedAction(kind)
  }

  function openSelectedAction(kind: ChannelDialogKind): void {
    const channel = rows.value.find(item => selectedIds.value.has(item.id))
    if (!channel || isMutating.value) return

    selectedChannel.value = { id: channel.id, name: channel.name, handle: channel.handle }
    rejectReason.value = ''
    mutationError.value = null
    activeDialog.value = kind
  }

  function closeActionDialog(): void {
    if (!isMutating.value) {
      activeDialog.value = null
      mutationError.value = null
    }
  }

  function updateRejectReason(value: string): void {
    rejectReason.value = value
  }

  async function confirmAction(): Promise<void> {
    const kind = activeDialog.value
    const channel = selectedChannel.value
    if (!kind || !channel || isMutating.value || selectedIdList.value.length === 0) {
      return
    }

    if (kind === 'reject' && !canSubmitRejection.value) {
      return
    }

    isMutating.value = true
    mutationError.value = null
    const submittedCount = selectedIdList.value.length

    try {
      if (kind === 'approve') {
        await approveChannels(selectedIdList.value)
      } else {
        await rejectChannels(selectedIdList.value, rejectReason.value.trim())
      }
    } catch (error) {
      mutationError.value = mutationErrorMessage(error)
      isMutating.value = false
      return
    }

    isMutating.value = false
    activeDialog.value = null
    rejectReason.value = ''
    await loadPending()
    await loadReviewCounts()
    showToast({
      tone: 'success',
      message: kind === 'approve'
        ? `${submittedCount} channel${submittedCount === 1 ? '' : 's'} approved. Backfill will start automatically.`
        : `${submittedCount} channel${submittedCount === 1 ? '' : 's'} rejected. They were removed from pending review.`,
    })
  }

  function clearSelection(): void {
    selectedIds.value = new Set()
  }

  function toggleRowSelection(channelId: string): void {
    const next = new Set(selectedIds.value)
    if (next.has(channelId)) next.delete(channelId)
    else next.add(channelId)
    selectedIds.value = next
  }

  function toggleVisibleSelection(): void {
    const visibleIds = rows.value.map(channel => channel.id)
    const allSelected = visibleIds.length > 0 && visibleIds.every(id => selectedIds.value.has(id))
    const next = new Set(selectedIds.value)

    for (const id of visibleIds) {
      if (allSelected) next.delete(id)
      else next.add(id)
    }

    selectedIds.value = next
  }

  function updateEvidenceFailure(channelId: string, failed: boolean): void {
    const next = { ...evidenceFailed.value }

    if (failed) {
      next[channelId] = true
    } else {
      delete next[channelId]
    }

    evidenceFailed.value = next
  }

  return {
    searchInput,
    rowsPerPage,
    currentPage,
    rows,
    totalCount,
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
    canSubmitRejection,
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
  }
}

function mutationErrorMessage(error: unknown): string {
  if (error instanceof ApiError) {
    if (error.status === 409) {
      return MUTATION_CONFLICT_MESSAGE
    }

    if (error.status === 404) {
      return MUTATION_MISSING_MESSAGE
    }

    // 429 (daily approved-channel maximum or exhausted request quota) and 503 (quota window not ready) already
    // carry a readable operator message from the backend.
    if ((error.status === 429 || error.status === 503) && typeof error.body === 'object' && error.body !== null) {
      const detail = (error.body as { detail?: unknown }).detail
      if (typeof detail === 'string' && detail.length > 0) {
        return detail
      }
    }

    if (error.status === 400 && typeof error.body === 'object' && error.body !== null) {
      const messages = (error.body as ApiValidationProblem).errors?.reason
      if (messages && messages.length > 0) {
        return messages[0]
      }
    }
  }

  return MUTATION_FALLBACK_MESSAGE
}
