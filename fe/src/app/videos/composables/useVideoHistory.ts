import { computed, ref } from 'vue'
import { getVideoHistory } from '@/app/videos/api/videos-api'
import { toVideoHistoryRow } from '@/app/videos/data/video-history.mapper'
import type {
  VideoHistoryPagination,
  VideoHistoryRow,
  VideoHistoryState,
  VideoHistoryTarget,
} from '@/app/videos/types/video-history.types'

const INITIAL_PAGE = 1
const DEFAULT_PAGE_SIZE = 10
const MAX_DISPLAYED_PAGES = 7

const LOADING_MESSAGE = 'Loading history…'
const EMPTY_MESSAGE = 'No history has been captured for this video yet.'
const FAILURE_MESSAGE = "Couldn't load this video's history."

/**
 * On-demand history dialog state. The request begins only when the operator opens the dialog for a row, and
 * every open/close bumps the request id so a slow response for a previous row can never render as stale
 * history. History is never prefetched for visible rows.
 */
export function useVideoHistory() {
  const open = ref(false)
  const target = ref<VideoHistoryTarget | null>(null)
  const rows = ref<VideoHistoryRow[]>([])
  const currentPage = ref(INITIAL_PAGE)
  const pageSize = ref(DEFAULT_PAGE_SIZE)
  const totalCount = ref(0)
  const loading = ref(false)
  const requestFailed = ref(false)

  let requestId = 0

  const state = computed<VideoHistoryState>(() => {
    if (loading.value) return 'loading'
    if (requestFailed.value) return 'error'
    return rows.value.length === 0 ? 'empty' : 'ready'
  })

  const stateMessage = computed(() => {
    if (state.value === 'loading') return LOADING_MESSAGE
    if (state.value === 'error') return FAILURE_MESSAGE
    if (state.value === 'empty') return EMPTY_MESSAGE
    return ''
  })

  const pagination = computed<VideoHistoryPagination>(() =>
    buildPagination(currentPage.value, pageSize.value, totalCount.value),
  )

  function openFor(next: VideoHistoryTarget): void {
    target.value = next
    open.value = true
    rows.value = []
    totalCount.value = 0
    currentPage.value = INITIAL_PAGE
    requestFailed.value = false
    void load()
  }

  function close(): void {
    // Invalidate any in-flight response so a reopened dialog never shows the previous row's history.
    requestId += 1
    open.value = false
    target.value = null
    rows.value = []
    totalCount.value = 0
    loading.value = false
    requestFailed.value = false
  }

  async function load(): Promise<void> {
    const currentTarget = target.value
    if (currentTarget === null) return

    const currentRequest = ++requestId
    loading.value = true
    requestFailed.value = false

    try {
      const response = await getVideoHistory(currentTarget.videoId, {
        page: currentPage.value,
        pageSize: pageSize.value,
      })

      if (currentRequest !== requestId) return

      rows.value = response.items.map(toVideoHistoryRow)
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

  function goToPage(page: number): void {
    if (page === currentPage.value || page < 1) return

    currentPage.value = page
    void load()
  }

  function retry(): void {
    void load()
  }

  return {
    open,
    target,
    rows,
    currentPage,
    pageSize,
    pagination,
    state,
    stateMessage,
    openFor,
    close,
    goToPage,
    retry,
  }
}

function buildPagination(page: number, currentPageSize: number, totalCount: number): VideoHistoryPagination {
  if (totalCount <= 0) {
    return { summary: 'No snapshots', pageCount: 0, displayPages: [] }
  }

  const pageCount = Math.max(1, Math.ceil(totalCount / currentPageSize))
  const first = (page - 1) * currentPageSize + 1
  const last = Math.min(page * currentPageSize, totalCount)

  return {
    summary: `Showing ${first}-${last} of ${totalCount} snapshots`,
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
