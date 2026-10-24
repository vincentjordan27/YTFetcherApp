import type { BackfillRunPagination } from '@/app/backfill-runs/types/backfill-run.types'

/** Every run ledger starts on page one. */
export const BACKFILL_RUN_INITIAL_PAGE = 1

/** Debounce applied to the run search box. */
export const BACKFILL_RUN_SEARCH_DEBOUNCE_MS = 300

/** Above this page count the pagination collapses its middle range into ellipses. */
const MAX_DISPLAYED_PAGES = 7

/** Builds the pagination view model from the server's `totalCount`. */
export function buildBackfillRunPagination(
  page: number,
  pageSize: number,
  totalCount: number,
): BackfillRunPagination {
  if (totalCount <= 0) {
    return { summary: 'Showing 0 of 0 runs', pageCount: 0, displayPages: [] }
  }

  const pageCount = Math.max(1, Math.ceil(totalCount / pageSize))
  const first = (page - 1) * pageSize + 1
  const last = Math.min(page * pageSize, totalCount)

  return {
    summary: `Showing ${first}-${last} of ${totalCount} runs`,
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
