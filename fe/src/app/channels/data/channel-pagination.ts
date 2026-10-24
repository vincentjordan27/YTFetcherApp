import type { ChannelPaginationDesign } from '@/app/channels/types/channel-review'

/** Every ledger starts on page one. */
export const CHANNEL_INITIAL_PAGE = 1

/** Debounce applied to the ledger search boxes. */
export const CHANNEL_SEARCH_DEBOUNCE_MS = 300

/** Above this page count the pagination collapses its middle range into ellipses. */
const MAX_DISPLAYED_PAGES = 7

/**
 * Builds the pagination view model for a channel ledger. It is the one pure calculation shared by the
 * pending ledger and the approved/rejected ledger, so their summary and ellipsis behavior cannot drift.
 */
export function buildChannelPagination(
  page: number,
  pageSize: number,
  totalCount: number,
): ChannelPaginationDesign {
  if (totalCount <= 0) {
    return { summary: 'Showing 0 of 0 channels', pageCount: 0, displayPages: [] }
  }

  const pageCount = Math.max(1, Math.ceil(totalCount / pageSize))
  const first = (page - 1) * pageSize + 1
  const last = Math.min(page * pageSize, totalCount)

  return {
    summary: `Showing ${first}-${last} of ${totalCount} channels`,
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
