import { computed, onBeforeUnmount, onMounted, ref } from 'vue'
import {
  createDiscoveryKeyword,
  disableDiscoveryKeyword,
  enableDiscoveryKeyword,
  getDiscoveryKeywords,
  removeDiscoveryKeyword,
} from '@/app/keywords/api/discovery-keywords-api'
import type {
  KeywordProblem,
  KeywordValidationProblem,
} from '@/app/keywords/api/types/discovery-keywords'
import { toKeywordListItem } from '@/app/keywords/data/keyword.mapper'
import {
  KEYWORD_INITIAL_PAGE,
  KEYWORD_SEARCH_DEBOUNCE_MS,
  buildKeywordPagination,
} from '@/app/keywords/data/keyword-pagination'
import type {
  KeywordDialogKind,
  KeywordListItem,
  KeywordTableState,
} from '@/app/keywords/types/keyword.types'
import { ApiError } from '@/common/api/api-client'
import { useToast } from '@/common/composables/useToast'
import { getInitialTablePageSize } from '@/common/data/initial-table-page-size'

const KEYWORD_MAX_LENGTH = 200

const MUTATION_CONFLICT_FALLBACK =
  'This keyword already exists or was previously removed. Refresh the list and try again.'
const MUTATION_MISSING_MESSAGE = 'This keyword no longer exists. Refresh the list and try again.'
const MUTATION_FALLBACK_MESSAGE = "Couldn't save the change. Check your connection and try again."

const LIST_LOADING_MESSAGE = 'Loading discovery keywords…'
const LIST_EMPTY_MESSAGE = 'No discovery keywords match this search.'
const LIST_FAILURE_MESSAGE = "Couldn't load discovery keywords."

/**
 * Live state of the discovery keyword ledger. It owns the list request lifecycle, the dialog state, and
 * the per-action pending flag; the server stays the source of truth for rows, `totalCount`, and
 * `activeCount`, so nothing here guesses a count or a status after a mutation.
 */
export function useKeywordList() {
  const searchInput = ref('')
  const searchText = ref('')
  const itemsPerPage = ref(getInitialTablePageSize())
  const currentPage = ref(KEYWORD_INITIAL_PAGE)
  const rows = ref<KeywordListItem[]>([])
  const totalCount = ref(0)
  const activeCount = ref(0)
  const loading = ref(false)
  const requestFailed = ref(false)
  const dialogKind = ref<KeywordDialogKind>(null)
  const dialogKeyword = ref<KeywordListItem | null>(null)
  const newKeywordTerm = ref('')
  const isMutating = ref(false)
  const pendingKeywordId = ref<string | null>(null)
  const mutationError = ref<string | null>(null)
  const { showToast } = useToast()

  let listRequestId = 0
  let searchTimer: ReturnType<typeof window.setTimeout> | undefined

  const activeCountLabel = computed(() => `${activeCount.value} active keywords`)

  const pagination = computed(() =>
    buildKeywordPagination(currentPage.value, itemsPerPage.value, totalCount.value),
  )

  const tableState = computed<KeywordTableState>(() => {
    if (loading.value) {
      return { state: 'loading', message: LIST_LOADING_MESSAGE }
    }

    if (requestFailed.value) {
      return { state: 'error', message: LIST_FAILURE_MESSAGE }
    }

    if (rows.value.length === 0) {
      return { state: 'empty', message: LIST_EMPTY_MESSAGE }
    }

    return { state: 'ready', message: '' }
  })

  const canSubmitKeyword = computed(() => {
    const term = newKeywordTerm.value.trim()
    return term.length > 0 && term.length <= KEYWORD_MAX_LENGTH
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
      const response = await getDiscoveryKeywords({
        page: currentPage.value,
        pageSize: itemsPerPage.value,
        query: searchText.value || undefined,
      })

      if (requestId !== listRequestId) {
        return
      }

      // A removal can leave the current page past the end, and the server is the only source of truth
      // for that. Exactly one replacement request, so an invalid range is never rendered.
      const lastValidPage = Math.max(
        KEYWORD_INITIAL_PAGE,
        Math.ceil(response.totalCount / itemsPerPage.value),
      )

      if (currentPage.value > lastValidPage) {
        currentPage.value = lastValidPage
        loading.value = false
        await load()
        return
      }

      rows.value = response.items.map(toKeywordListItem)
      totalCount.value = response.totalCount
      activeCount.value = response.activeCount
    } catch {
      if (requestId !== listRequestId) {
        return
      }

      rows.value = []
      totalCount.value = 0
      activeCount.value = 0
      requestFailed.value = true
    } finally {
      if (requestId === listRequestId) {
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
      currentPage.value = KEYWORD_INITIAL_PAGE
      void load()
    }, KEYWORD_SEARCH_DEBOUNCE_MS)
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
    if (!Number.isFinite(pageSize) || pageSize < 1 || pageSize === itemsPerPage.value) {
      return
    }

    itemsPerPage.value = pageSize
    currentPage.value = KEYWORD_INITIAL_PAGE
    void load()
  }

  function refresh(): void {
    void load()
  }

  function openAddDialog(): void {
    newKeywordTerm.value = ''
    mutationError.value = null
    dialogKind.value = 'add'
  }

  function requestDisable(keyword: KeywordListItem): void {
    dialogKeyword.value = keyword
    mutationError.value = null
    dialogKind.value = 'disable'
  }

  function requestRemove(keyword: KeywordListItem): void {
    dialogKeyword.value = keyword
    mutationError.value = null
    dialogKind.value = 'remove'
  }

  function closeDialog(): void {
    if (isMutating.value) {
      return
    }

    dialogKind.value = null
    dialogKeyword.value = null
    newKeywordTerm.value = ''
    mutationError.value = null
  }

  function updateNewKeywordTerm(value: string): void {
    newKeywordTerm.value = value
  }

  /** An added keyword is newest, so the operator sees it by returning to the first page. */
  async function confirmAdd(): Promise<void> {
    if (isMutating.value || !canSubmitKeyword.value) {
      return
    }

    const succeeded = await mutate(() => createDiscoveryKeyword(newKeywordTerm.value.trim()))

    if (succeeded) {
      currentPage.value = KEYWORD_INITIAL_PAGE
      await load()
      showToast({ tone: 'success', message: 'Keyword added. The next discovery run will use it.' })
    }
  }

  async function confirmDisable(): Promise<void> {
    const keyword = dialogKeyword.value
    if (!keyword || isMutating.value) {
      return
    }

    const succeeded = await mutate(() => disableDiscoveryKeyword(keyword.id), keyword.id)

    if (succeeded) {
      await load()
      showToast({ tone: 'success', message: 'Keyword disabled. Existing channels and videos stay unchanged.' })
    }
  }

  /** Enabling has no confirmation dialog: the row action applies it directly. */
  async function enableKeyword(keyword: KeywordListItem): Promise<void> {
    if (isMutating.value || keyword.active) {
      return
    }

    const succeeded = await mutate(() => enableDiscoveryKeyword(keyword.id), keyword.id)

    if (succeeded) {
      await load()
      showToast({ tone: 'success', message: 'Keyword enabled. The next discovery run will use it.' })
    }
  }

  async function confirmRemove(): Promise<void> {
    const keyword = dialogKeyword.value
    if (!keyword || isMutating.value) {
      return
    }

    const succeeded = await mutate(() => removeDiscoveryKeyword(keyword.id), keyword.id)

    if (succeeded) {
      await load()
      showToast({ tone: 'success', message: 'Keyword removed from discovery.' })
    }
  }

  /**
   * Runs one mutation behind the shared guards: a second submit is refused while a request is in
   * flight, a failure keeps the dialog open with a readable message, and the acting row is marked
   * pending without freezing the rest of the ledger.
   */
  async function mutate(action: () => Promise<unknown>, keywordId?: string): Promise<boolean> {
    isMutating.value = true
    pendingKeywordId.value = keywordId ?? null
    mutationError.value = null

    try {
      await action()
    } catch (error) {
      mutationError.value = mutationErrorMessage(error)
      return false
    } finally {
      isMutating.value = false
      pendingKeywordId.value = null
    }

    dialogKind.value = null
    dialogKeyword.value = null
    newKeywordTerm.value = ''

    return true
  }

  return {
    activeCountLabel,
    canSubmitKeyword,
    closeDialog,
    confirmAdd,
    confirmDisable,
    confirmRemove,
    currentPage,
    dialogKind,
    dialogKeyword,
    enableKeyword,
    goToPage,
    isMutating,
    itemsPerPage,
    mutationError,
    newKeywordTerm,
    openAddDialog,
    pagination,
    pendingKeywordId,
    refresh,
    requestDisable,
    requestRemove,
    rows,
    searchInput,
    setItemsPerPage,
    tableState,
    updateNewKeywordTerm,
    updateSearchInput,
  }
}

/**
 * Renders the server's readable sentence as received. Only an unexpected failure falls back to the
 * generic non-technical message, so a duplicate, a missing keyword, or a validation problem is never
 * replaced by a guess.
 */
function mutationErrorMessage(error: unknown): string {
  if (error instanceof ApiError) {
    if (error.status === 409) {
      return problemDetail(error.body) ?? MUTATION_CONFLICT_FALLBACK
    }

    if (error.status === 404) {
      return MUTATION_MISSING_MESSAGE
    }

    if (error.status === 400) {
      const messages = validationErrors(error.body)?.keyword
      if (messages && messages.length > 0) {
        return messages[0]
      }
    }
  }

  return MUTATION_FALLBACK_MESSAGE
}

function problemDetail(body: unknown): string | null {
  if (typeof body !== 'object' || body === null) {
    return null
  }

  const detail = (body as KeywordProblem).detail
  return typeof detail === 'string' && detail.length > 0 ? detail : null
}

function validationErrors(body: unknown): Readonly<Record<string, readonly string[]>> | undefined {
  if (typeof body !== 'object' || body === null) {
    return undefined
  }

  return (body as KeywordValidationProblem).errors
}
