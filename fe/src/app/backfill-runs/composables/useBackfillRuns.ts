import { computed, onBeforeUnmount, onMounted, ref } from 'vue'
import { getBackfillRuns, retryBackfill } from '@/app/backfill-runs/api/backfill-runs-api'
import type {
  BackfillRunProblem,
  BackfillRunValidationProblem,
} from '@/app/backfill-runs/api/types/backfill-runs'
import { toBackfillRunRow } from '@/app/backfill-runs/data/backfill-run.mapper'
import {
  BACKFILL_RUN_INITIAL_PAGE,
  BACKFILL_RUN_SEARCH_DEBOUNCE_MS,
  buildBackfillRunPagination,
} from '@/app/backfill-runs/data/backfill-run-pagination'
import type {
  BackfillRunDialogKind,
  BackfillRunRow,
  BackfillRunTableState,
} from '@/app/backfill-runs/types/backfill-run.types'
import { ApiError } from '@/common/api/api-client'
import { useToast } from '@/common/composables/useToast'
import { getInitialTablePageSize } from '@/common/data/initial-table-page-size'

const RETRY_CONFLICT_FALLBACK =
  'This channel is no longer eligible for a retry. The list was refreshed.'
const RETRY_FALLBACK_MESSAGE = "Couldn't retry the backfill. Check your connection and try again."

const LIST_LOADING_MESSAGE = 'Loading backfill runs\u2026'
const LIST_EMPTY_MESSAGE = 'No backfill runs match these filters.'
const LIST_FAILURE_MESSAGE = "Couldn't load backfill runs."

/** Filter values offered by the toolbar, in the order the API accepts. */
export const BACKFILL_RUN_STATUS_OPTIONS = ['RUNNING', 'SUCCEEDED', 'FAILED', 'ABANDONED'] as const
export const BACKFILL_RUN_TRIGGER_OPTIONS = ['APPROVAL', 'DAILY_PUMP', 'RECOVERY'] as const

/**
 * Live state of the backfill run ledger. The server owns the rows, the total count, and every eligibility
 * decision, so nothing here guesses a page, a count, or whether a retry will be accepted.
 */
export function useBackfillRuns() {
  const searchInput = ref('')
  const searchText = ref('')
  const statusFilter = ref('')
  const triggerFilter = ref('')
  const dateFromFilter = ref('')
  const dateToFilter = ref('')
  const rowsPerPage = ref(getInitialTablePageSize())
  const currentPage = ref(BACKFILL_RUN_INITIAL_PAGE)
  const rows = ref<BackfillRunRow[]>([])
  const totalCount = ref(0)
  const loading = ref(false)
  const requestFailed = ref(false)
  const dialogKind = ref<BackfillRunDialogKind>(null)
  const selectedRow = ref<BackfillRunRow | null>(null)
  const isRetrying = ref(false)
  const retryError = ref<string | null>(null)
  const { showToast } = useToast()

  let listRequestId = 0
  let searchTimer: ReturnType<typeof window.setTimeout> | undefined

  const pagination = computed(() =>
    buildBackfillRunPagination(currentPage.value, rowsPerPage.value, totalCount.value),
  )

  const tableState = computed<BackfillRunTableState>(() => {
    if (loading.value) {
      return { state: 'loading', message: LIST_LOADING_MESSAGE }
    }

    if (requestFailed.value) {
      return { state: 'error', message: LIST_FAILURE_MESSAGE }
    }

    return rows.value.length === 0
      ? { state: 'empty', message: LIST_EMPTY_MESSAGE }
      : { state: 'ready', message: '' }
  })

  onMounted(() => {
    void load()
  })

  // Unmount invalidates the debounce and any in-flight list request, so a late response writes no list state.
  onBeforeUnmount(() => {
    window.clearTimeout(searchTimer)
    searchTimer = undefined
    listRequestId += 1
  })

  async function load(): Promise<void> {
    const requestId = ++listRequestId
    loading.value = true
    requestFailed.value = false

    try {
      const response = await getBackfillRuns({
        page: currentPage.value,
        pageSize: rowsPerPage.value,
        query: searchText.value || undefined,
        status: statusFilter.value || undefined,
        triggerSource: triggerFilter.value || undefined,
        dateFrom: dateFromFilter.value || undefined,
        dateTo: dateToFilter.value || undefined,
      })

      if (requestId !== listRequestId) {
        return
      }

      // A removal or a filter change can leave the current page past the end, and only the server knows.
      const lastValidPage = Math.max(
        BACKFILL_RUN_INITIAL_PAGE,
        Math.ceil(response.totalCount / rowsPerPage.value),
      )

      if (currentPage.value > lastValidPage) {
        currentPage.value = lastValidPage
        loading.value = false
        await load()
        return
      }

      totalCount.value = response.totalCount
      rows.value = response.items.map(toBackfillRunRow)
    } catch {
      if (requestId !== listRequestId) {
        return
      }

      rows.value = []
      totalCount.value = 0
      requestFailed.value = true
    } finally {
      if (requestId === listRequestId) {
        loading.value = false
      }
    }
  }

  function updateSearchInput(value: string): void {
    searchInput.value = value
    window.clearTimeout(searchTimer)

    searchTimer = window.setTimeout(() => {
      const next = value.trim()
      if (next === searchText.value) {
        return
      }

      searchText.value = next
      currentPage.value = BACKFILL_RUN_INITIAL_PAGE
      void load()
    }, BACKFILL_RUN_SEARCH_DEBOUNCE_MS)
  }

  /** Every filter restarts at page one, because the previous page may no longer exist. */
  function applyStatusFilter(value: string): void {
    statusFilter.value = value
    currentPage.value = BACKFILL_RUN_INITIAL_PAGE
    void load()
  }

  function applyTriggerFilter(value: string): void {
    triggerFilter.value = value
    currentPage.value = BACKFILL_RUN_INITIAL_PAGE
    void load()
  }

  function applyDateFromFilter(value: string): void {
    dateFromFilter.value = value
    currentPage.value = BACKFILL_RUN_INITIAL_PAGE
    void load()
  }

  function applyDateToFilter(value: string): void {
    dateToFilter.value = value
    currentPage.value = BACKFILL_RUN_INITIAL_PAGE
    void load()
  }

  function goToPage(page: number): void {
    if (page === currentPage.value || page < 1) {
      return
    }

    currentPage.value = page
    void load()
  }

  function setItemsPerPage(value: string): void {
    const pageSize = Number(value)
    if (!Number.isFinite(pageSize) || pageSize < 1 || pageSize === rowsPerPage.value) {
      return
    }

    rowsPerPage.value = pageSize
    currentPage.value = BACKFILL_RUN_INITIAL_PAGE
    void load()
  }

  function refresh(): void {
    void load()
  }

  /** A failed row opens the read-only detail dialog; it is not the rejection dialog. */
  function openRow(row: BackfillRunRow): void {
    if (row.statusTone !== 'failed') {
      return
    }

    selectedRow.value = row
    retryError.value = null
    dialogKind.value = 'detail'
  }

  function requestRetry(row: BackfillRunRow): void {
    selectedRow.value = row
    retryError.value = null
    dialogKind.value = 'retry'
  }

  function closeDialog(): void {
    if (isRetrying.value) {
      return
    }

    dialogKind.value = null
    selectedRow.value = null
    retryError.value = null
  }

  async function confirmRetry(): Promise<void> {
    const row = selectedRow.value
    if (row === null || isRetrying.value) {
      return
    }

    isRetrying.value = true
    retryError.value = null

    try {
      await retryBackfill(row.channelId)
    } catch (error) {
      // The server stays authoritative: refresh so the operator sees the real state, then show its message.
      retryError.value = retryErrorMessage(error)
      isRetrying.value = false
      await load()
      return
    }

    isRetrying.value = false
    dialogKind.value = null
    selectedRow.value = null
    await load()
    showToast({ tone: 'success', message: 'Backfill retry requested. The pump will pick it up when quota allows.' })
  }

  return {
    applyDateFromFilter,
    applyDateToFilter,
    applyStatusFilter,
    applyTriggerFilter,
    closeDialog,
    confirmRetry,
    currentPage,
    dateFromFilter,
    dateToFilter,
    dialogKind,
    goToPage,
    isRetrying,
    itemsPerPage: rowsPerPage,
    openRow,
    pagination,
    refresh,
    requestRetry,
    retryError,
    rows,
    rowsPerPage,
    searchInput,
    selectedRow,
    setItemsPerPage,
    statusFilter,
    tableState,
    triggerFilter,
    updateSearchInput,
  }
}

/** Renders the server's readable sentence as received, falling back only for an unexpected failure. */
function retryErrorMessage(error: unknown): string {
  if (error instanceof ApiError) {
    if (error.status === 409) {
      return problemDetail(error.body) ?? RETRY_CONFLICT_FALLBACK
    }

    if (error.status === 400) {
      const messages = validationErrors(error.body)?.channelId
      if (messages && messages.length > 0) {
        return messages[0]
      }
    }
  }

  return RETRY_FALLBACK_MESSAGE
}

function problemDetail(body: unknown): string | null {
  if (typeof body !== 'object' || body === null) {
    return null
  }

  const detail = (body as BackfillRunProblem).detail
  return typeof detail === 'string' && detail.length > 0 ? detail : null
}

function validationErrors(body: unknown): Readonly<Record<string, readonly string[]>> | undefined {
  if (typeof body !== 'object' || body === null) {
    return undefined
  }

  return (body as BackfillRunValidationProblem).errors
}
