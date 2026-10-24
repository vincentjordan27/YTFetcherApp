import { computed, onBeforeUnmount, onMounted, ref } from 'vue'
import { getReferenceVideos, unmarkVideoReference } from '@/app/videos/api/videos-api'
import { toReferenceVideoRow } from '@/app/videos/data/reference-video.mapper'
import { nextVideoSort, toVideoSortLabel } from '@/app/videos/data/video-sort.mapper'
import type { ReferenceVideoPagination, ReferenceVideoRow } from '@/app/videos/types/reference-video.types'
import { DEFAULT_VIDEO_SORT, type VideoSort, type VideoSortField } from '@/app/videos/types/video-sort.types'
import { ApiError } from '@/common/api/api-client'
import { useToast } from '@/common/composables/useToast'
import { getInitialTablePageSize } from '@/common/data/initial-table-page-size'

const INITIAL_PAGE = 1
const SEARCH_DEBOUNCE_MS = 300
const MAX_DISPLAYED_PAGES = 7

const LIST_LOADING_MESSAGE = 'Loading videos…'
const LIST_EMPTY_MESSAGE = 'No videos match this filter.'
const LIST_FAILURE_MESSAGE = "Couldn't load videos."

const REMOVE_SUCCESS_MESSAGE = 'Returned to active tracking.'
const REMOVE_FAILURE_MESSAGE = "Couldn't return the video to active tracking. Check your connection and try again."

/**
 * Server-paged state for the live Reference tab: 300 ms debounced server search, one-based paging, a page
 * clamp that issues exactly one replacement request, the server-side sort contract, and the per-row
 * remove-reference command. Search, page size, and mutation refetches all preserve the active sort.
 */
export function useReferenceVideos() {
  const searchInput = ref('')
  const searchText = ref('')
  const rowsPerPage = ref(getInitialTablePageSize())
  const currentPage = ref(INITIAL_PAGE)
  const sort = ref<VideoSort>({ ...DEFAULT_VIDEO_SORT })
  const rows = ref<ReferenceVideoRow[]>([])
  const totalCount = ref(0)
  const loading = ref(false)
  const requestFailed = ref(false)
  const removePending = ref(false)
  const { showToast } = useToast()

  let requestId = 0
  let searchTimer: ReturnType<typeof window.setTimeout> | undefined

  const pagination = computed<ReferenceVideoPagination>(() =>
    buildPagination(currentPage.value, rowsPerPage.value, totalCount.value),
  )

  const tableState = computed(() => {
    if (loading.value) return { state: 'loading' as const, message: LIST_LOADING_MESSAGE }
    if (requestFailed.value) return { state: 'error' as const, message: LIST_FAILURE_MESSAGE }
    if (rows.value.length === 0) return { state: 'empty' as const, message: LIST_EMPTY_MESSAGE }
    return { state: 'ready' as const, message: '' }
  })

  /** Informational toolbar text; the header buttons are the only sort control. */
  const sortLabel = computed(() => toVideoSortLabel(sort.value))

  onMounted(() => {
    void load()
  })

  // Leaving the feature cancels the pending debounce and invalidates any in-flight list request, so a late
  // reply can never write rows, counts, busy state, or error state into an unmounted composable.
  onBeforeUnmount(() => {
    window.clearTimeout(searchTimer)
    searchTimer = undefined
    requestId += 1
  })

  async function load(): Promise<void> {
    const currentRequest = ++requestId
    loading.value = true
    requestFailed.value = false

    try {
      const response = await getReferenceVideos({
        page: currentPage.value,
        pageSize: rowsPerPage.value,
        query: searchText.value || undefined,
        sortBy: sort.value.field,
        sortDirection: sort.value.direction,
      })

      if (currentRequest !== requestId) return

      const lastValidPage = Math.max(INITIAL_PAGE, Math.ceil(response.totalCount / rowsPerPage.value))
      if (currentPage.value > lastValidPage) {
        // Exactly one replacement request so an invalid range is never rendered.
        currentPage.value = lastValidPage
        loading.value = false
        await load()
        return
      }

      rows.value = response.items.map(toReferenceVideoRow)
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
      currentPage.value = INITIAL_PAGE
      void load()
    }, SEARCH_DEBOUNCE_MS)
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
    currentPage.value = INITIAL_PAGE
    void load()
  }

  function retry(): void {
    void load()
  }

  /**
   * One header activation: a new column starts ascending, the active column flips. The server owns the
   * order, so paging restarts at one and the list is refetched.
   */
  function setSort(field: VideoSortField): void {
    sort.value = nextVideoSort(sort.value, field)
    currentPage.value = INITIAL_PAGE
    void load()
  }

  /**
   * Removes one row from Reference. The list is never spliced locally: a success refetches server truth and
   * lets the existing page clamp decide the page, and a failure leaves the rendered list untouched.
   */
  async function removeReference(row: ReferenceVideoRow): Promise<void> {
    if (removePending.value) return

    removePending.value = true

    try {
      await unmarkVideoReference(row.id)
    } catch (caught) {
      removePending.value = false
      showToast({ tone: 'failure', message: readableRemoveFailure(caught) })
      return
    }

    await load()
    removePending.value = false
    showToast({ tone: 'success', message: REMOVE_SUCCESS_MESSAGE })
  }

  return {
    searchInput,
    currentPage,
    rowsPerPage,
    sort,
    sortLabel,
    rows,
    totalCount,
    pagination,
    tableState,
    removePending,
    updateSearchInput,
    goToPage,
    updateRowsPerPage,
    setSort,
    retry,
    removeReference,
  }
}

/**
 * A removed row that the server no longer considers reference comes back as a readable 404 detail; that text
 * is shown as-is. Anything unexpected falls back to one fixed, non-technical sentence.
 */
function readableRemoveFailure(error: unknown): string {
  if (error instanceof ApiError && (error.status === 400 || error.status === 404 || error.status === 409)) {
    const body = error.body
    if (typeof body === 'object' && body !== null && 'detail' in body) {
      const detail = (body as { detail?: unknown }).detail
      if (typeof detail === 'string' && detail.length > 0) {
        return detail
      }
    }
  }

  return REMOVE_FAILURE_MESSAGE
}

function buildPagination(page: number, pageSize: number, totalCount: number): ReferenceVideoPagination {
  if (totalCount <= 0) {
    return { summary: 'Showing 0 of 0 videos', pageCount: 0, displayPages: [] }
  }

  const pageCount = Math.max(1, Math.ceil(totalCount / pageSize))
  const first = (page - 1) * pageSize + 1
  const last = Math.min(page * pageSize, totalCount)

  return {
    summary: `Showing ${first}-${last} of ${totalCount} videos`,
    pageCount,
    displayPages: buildDisplayPages(page, pageCount),
  }
}

function buildDisplayPages(page: number, pageCount: number): (number | 'ellipsis')[] {
  if (pageCount <= MAX_DISPLAYED_PAGES) {
    return Array.from({ length: pageCount }, (_, index) => index + 1)
  }

  const pages: (number | 'ellipsis')[] = [1]
  const windowStart = Math.max(2, page - 1)
  const windowEnd = Math.min(pageCount - 1, page + 1)

  if (windowStart > 2) {
    pages.push('ellipsis')
  }

  for (let candidate = windowStart; candidate <= windowEnd; candidate++) {
    pages.push(candidate)
  }

  if (windowEnd < pageCount - 1) {
    pages.push('ellipsis')
  }

  pages.push(pageCount)

  return pages
}
