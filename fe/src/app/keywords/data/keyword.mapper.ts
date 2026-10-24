import type { DiscoveryKeywordItem } from '@/app/keywords/api/types/discovery-keywords'
import type { KeywordListItem } from '@/app/keywords/types/keyword.types'

const dateFormatter = new Intl.DateTimeFormat('en-GB', {
  day: '2-digit',
  month: 'short',
  year: 'numeric',
})

const timeFormatter = new Intl.DateTimeFormat('en-GB', {
  hour: '2-digit',
  minute: '2-digit',
  hour12: false,
})

/**
 * Formats an API timestamp with the approved visual baseline shape, for example `16 Sep 2026, 11:00`.
 * The value stays ISO/UTC everywhere else; only this presentation path localizes it.
 */
function formatKeywordDateTime(value: string): string {
  const date = new Date(value)
  if (Number.isNaN(date.getTime())) {
    return '—'
  }

  return `${dateFormatter.format(date)}, ${timeFormatter.format(date)}`
}

/** Maps one API keyword to the ledger row the approved table renders. */
export function toKeywordListItem(item: DiscoveryKeywordItem): KeywordListItem {
  return {
    id: item.id,
    term: item.keyword,
    active: item.isActive,
    createdAt: formatKeywordDateTime(item.createdAt),
    updatedAt: formatKeywordDateTime(item.modifiedAt),
  }
}
