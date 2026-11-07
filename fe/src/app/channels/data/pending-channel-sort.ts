import type { PendingChannelSort, PendingChannelSortField } from '@/app/channels/types/pending-channel-sort'

const SORT_LABELS: Readonly<Record<PendingChannelSortField, string>> = {
  name: 'Channel',
  subscriberCount: 'Subscribers',
  discoveredAt: 'Discovery date',
  evidenceCount: 'Evidence Shorts',
}

export function nextPendingChannelSort(current: PendingChannelSort, field: PendingChannelSortField): PendingChannelSort {
  if (current.field !== field) {
    return { field, direction: 'asc' }
  }

  return { field, direction: current.direction === 'asc' ? 'desc' : 'asc' }
}

export function toPendingChannelSortAriaLabel(field: PendingChannelSortField): string {
  return `Sort by ${SORT_LABELS[field]}`
}

export function toPendingChannelAriaSort(
  current: PendingChannelSort,
  field: PendingChannelSortField,
): 'ascending' | 'descending' | 'none' {
  if (current.field !== field) {
    return 'none'
  }

  return current.direction === 'asc' ? 'ascending' : 'descending'
}
