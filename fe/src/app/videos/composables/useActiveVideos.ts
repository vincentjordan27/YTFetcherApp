import { computed, onBeforeUnmount, onMounted, ref } from 'vue'
import { getActiveVideos, markVideosReference } from '@/app/videos/api/videos-api'
import { toActiveVideoRow } from '@/app/videos/data/active-video.mapper'
import { nextVideoSort, toVideoSortLabel } from '@/app/videos/data/video-sort.mapper'
import type { ActiveVideoPagination, ActiveVideoRow } from '@/app/videos/types/active-video.types'
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

const MARK_CONFLICT_MESSAGE = 'One or more selected videos are no longer active. Refresh the list and try again.'
const MARK_FAILURE_MESSAGE = "Couldn't mark the selected videos as reference. Check your connection and try again."
const MARK_SUCCESS_MESSAGE = 'Videos marked as reference.'

/**
 * Server-paged state for the live Active tab: 300 ms debounced server search, one-based paging, a
 * page clamp that issues exactly one replacement request, page-local reference selection, and the
 * server-side sort contract. Search, page size, and mutation refetches all preserve the active sort.
 */
export function useActiveVideos() {
  const searchInput = ref('')
  const searchText = ref('')
  const rowsPerPage = ref(getInitialTablePageSize())
  const currentPage = ref(INITIAL_PAGE)
  const sort = ref<VideoSort>({ ...DEFAULT_VIDEO_SORT })
  const rows = ref<ActiveVideoRow[]>([])
  const totalCount = ref(0)
  const loading = ref(false)
  const requestFailed = ref(false)
  const selectedIds = ref<ReadonlySet<string>>(new Set())
  const markReferenceOpen = ref(false)
  const markReferencePending = ref(false)
  const markReferenceError = ref<string | null>(null)
  const { showToast } = useToast()

  let requestId = 0
  let searchTimer: ReturnType<typeof window.setTimeout> | undefined

  const pagination = computed<ActiveVideoPagination>(() =>
    buildPagination(currentPage.value, rowsPerPage.value, totalCount.value),
  )

  const tableState = computed(() => {
    if (loading.value) return { state: 'loading' as const, message: LIST_LOADING_MESSAGE }
    if (requestFailed.value) return { state: 'error' as const, message: LIST_FAILURE_MESSAGE }
    if (rows.value.length === 0) return { state: 'empty' as const, message: LIST_EMPTY_MESSAGE }
    return { state: 'ready' as const, message: '' }
  })

  const selectedCount = computed(() => selectedIds.value.size)
  const selectedIdList = computed(() => [...selectedIds.value])
  const canMarkReference = computed(() => selectedIds.value.size > 0 && !markReferencePending.value)

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
      const response = await getActiveVideos({
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

      rows.value = response.items.map(toActiveVideoRow)
      totalCount.value = response.totalCount
      // A new page of results invalidates the previous page-local selection.
      clearSelection()
    } catch {
      if (currentRequest !== requestId) return

      rows.value = []
      totalCount.value = 0
      requestFailed.value = true
      clearSelection()
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
   * order, so paging restarts at one, the page-local selection is dropped, and the list is refetched.
   */
  function setSort(field: VideoSortField): void {
    sort.value = nextVideoSort(sort.value, field)
    currentPage.value = INITIAL_PAGE
    clearSelection()
    void load()
  }

  function clearSelection(): void {
    selectedIds.value = new Set()
  }

  function toggleRowSelection(videoId: string): void {
    const next = new Set(selectedIds.value)
    if (next.has(videoId)) {
      next.delete(videoId)
    } else {
      next.add(videoId)
    }

    selectedIds.value = next
  }

  /** Header checkbox: selects or clears every currently visible ready row. */
  function toggleVisibleSelection(): void {
    const visibleIds = rows.value.map(row => row.id)
    const allSelected = visibleIds.length > 0 && visibleIds.every(id => selectedIds.value.has(id))
    const next = new Set(selectedIds.value)

    for (const id of visibleIds) {
      if (allSelected) {
        next.delete(id)
      } else {
        next.add(id)
      }
    }

    selectedIds.value = next
  }

  function openMarkReference(): void {
    if (!canMarkReference.value) return
    markReferenceError.value = null
    markReferenceOpen.value = true
  }

  function openSingleMarkReference(videoId: string): void {
    if (markReferencePending.value) return

    selectedIds.value = new Set([videoId])
    openMarkReference()
  }

  function closeMarkReference(): void {
    if (markReferencePending.value) return
    markReferenceOpen.value = false
    markReferenceError.value = null
  }

  async function confirmMarkReference(): Promise<void> {
    if (!canMarkReference.value) return

    markReferencePending.value = true
    markReferenceError.value = null

    try {
      await markVideosReference(selectedIdList.value)
    } catch (caught) {
      markReferencePending.value = false

      if (caught instanceof ApiError && caught.status === 409) {
        // The list is stale: close, reload server truth, and surface the readable conflict.
        markReferenceOpen.value = false
        await load()
        showToast({ tone: 'failure', message: markConflictMessage(caught) })
        return
      }

      markReferenceError.value = MARK_FAILURE_MESSAGE
      return
    }

    markReferencePending.value = false
    markReferenceOpen.value = false
    // Reload clears the selection through the normal list-result path.
    await load()
    showToast({ tone: 'success', message: MARK_SUCCESS_MESSAGE })
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
    selectedIds,
    selectedIdList,
    selectedCount,
    canMarkReference,
    markReferenceOpen,
    markReferencePending,
    markReferenceError,
    updateSearchInput,
    goToPage,
    updateRowsPerPage,
    setSort,
    retry,
    clearSelection,
    toggleRowSelection,
    toggleVisibleSelection,
    openMarkReference,
    openSingleMarkReference,
    closeMarkReference,
    confirmMarkReference,
  }
}

function markConflictMessage(error: ApiError): string {
  const body = error.body
  if (typeof body === 'object' && body !== null && 'detail' in body) {
    const detail = (body as { detail?: unknown }).detail
    if (typeof detail === 'string' && detail.length > 0) {
      return detail
    }
  }

  return MARK_CONFLICT_MESSAGE
}

function buildPagination(page: number, pageSize: number, totalCount: number): ActiveVideoPagination {
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
