import { computed, onBeforeUnmount, onMounted, ref } from 'vue'
import { getApprovedChannels, getRejectedChannels } from '@/app/channels/api/channels-api'
import {
  CHANNEL_INITIAL_PAGE,
  CHANNEL_SEARCH_DEBOUNCE_MS,
  buildChannelPagination,
} from '@/app/channels/data/channel-pagination'
import { toApprovedChannelRow, toRejectedChannelRow } from '@/app/channels/data/channel-review.mapper'
import { useChannelReviewTabs } from '@/app/channels/composables/useChannelReviewTabs'
import type {
  ChannelLedgerVariant,
  ChannelPaginationDesign,
  ChannelReviewRow,
} from '@/app/channels/types/channel-review'
import { getInitialTablePageSize } from '@/common/data/initial-table-page-size'

const LIST_LOADING_MESSAGE = 'Loading channels…'
const LIST_EMPTY_MESSAGE = 'No channels match this filter.'
const LIST_FAILURE_MESSAGE = "Couldn't load channels."

interface LedgerPageRequest {
  readonly page: number
  readonly pageSize: number
  readonly query?: string
}

interface LedgerPage {
  readonly items: readonly ChannelReviewRow[]
  readonly totalCount: number
}

/**
 * Shared server-paged ledger state for the approved and rejected channel tabs: debounced server search, page
 * clamping with exactly one replacement request, and the real tab badges. The caller passes only the narrow
 * ledger variant; this composable owns which endpoint function and mapper that variant uses, so no view imports
 * an API function or a mapper.
 */
export function useChannelLedger(variant: ChannelLedgerVariant) {
  const { tabs, loadReviewCounts } = useChannelReviewTabs()

  const searchInput = ref('')
  const searchText = ref('')
  const rowsPerPage = ref(getInitialTablePageSize())
  const currentPage = ref(CHANNEL_INITIAL_PAGE)
  const rows = ref<ChannelReviewRow[]>([])
  const totalCount = ref(0)
  const loading = ref(false)
  const requestFailed = ref(false)

  let requestId = 0
  let searchTimer: ReturnType<typeof window.setTimeout> | undefined

  const pagination = computed<ChannelPaginationDesign>(() =>
    buildChannelPagination(currentPage.value, rowsPerPage.value, totalCount.value),
  )

  const tableState = computed(() => {
    if (loading.value) return { state: 'loading' as const, message: LIST_LOADING_MESSAGE }
    if (requestFailed.value) return { state: 'error' as const, message: LIST_FAILURE_MESSAGE }
    if (rows.value.length === 0) return { state: 'empty' as const, message: LIST_EMPTY_MESSAGE }
    return { state: 'ready' as const, message: '' }
  })

  onMounted(() => {
    void load()
  })

  // Unmount invalidates the debounce and any in-flight list request, so a late response writes no list state.
  onBeforeUnmount(() => {
    window.clearTimeout(searchTimer)
    searchTimer = undefined
    requestId += 1
  })

  /** The variant decides the endpoint and the mapper; the view never sees either. */
  async function fetchPage(request: LedgerPageRequest): Promise<LedgerPage> {
    if (variant === 'approved') {
      const response = await getApprovedChannels(request)
      return { items: response.items.map(toApprovedChannelRow), totalCount: response.totalCount }
    }

    const response = await getRejectedChannels(request)
    return { items: response.items.map(toRejectedChannelRow), totalCount: response.totalCount }
  }

  async function load(): Promise<void> {
    const currentRequest = ++requestId
    loading.value = true
    requestFailed.value = false

    try {
      const response = await fetchPage({
        page: currentPage.value,
        pageSize: rowsPerPage.value,
        query: searchText.value || undefined,
      })

      if (currentRequest !== requestId) return

      const lastValidPage = Math.max(CHANNEL_INITIAL_PAGE, Math.ceil(response.totalCount / rowsPerPage.value))
      if (currentPage.value > lastValidPage) {
        // Exactly one replacement request so an invalid range is never rendered.
        currentPage.value = lastValidPage
        loading.value = false
        await load()
        return
      }

      rows.value = [...response.items]
      totalCount.value = response.totalCount
    } catch {
      if (currentRequest !== requestId) return
      rows.value = []
      totalCount.value = 0
      requestFailed.value = true
    } finally {
      if (currentRequest === requestId) {
        loading.value = false
      }
    }
  }

  function updateSearchInput(value: string): void {
    searchInput.value = value
    window.clearTimeout(searchTimer)

    searchTimer = window.setTimeout(() => {
      const nextSearchText = value.trim()
      if (nextSearchText === searchText.value) return

      searchText.value = nextSearchText
      currentPage.value = CHANNEL_INITIAL_PAGE
      void load()
    }, CHANNEL_SEARCH_DEBOUNCE_MS)
  }

  function goToPage(page: number): void {
    if (page === currentPage.value || page < 1) return

    currentPage.value = page
    void load()
  }

  function updateRowsPerPage(value: string): void {
    const pageSize = Number(value)
    if (!Number.isFinite(pageSize) || pageSize < 1 || pageSize === rowsPerPage.value) return

    rowsPerPage.value = pageSize
    currentPage.value = CHANNEL_INITIAL_PAGE
    void load()
  }

  function retry(): void {
    void load()
  }

  async function refresh(): Promise<void> {
    await load()
    await loadReviewCounts()
  }

  return {
    tabs,
    searchInput,
    currentPage,
    rowsPerPage,
    rows,
    totalCount,
    pagination,
    tableState,
    updateSearchInput,
    goToPage,
    updateRowsPerPage,
    retry,
    reload: refresh,
  }
}
