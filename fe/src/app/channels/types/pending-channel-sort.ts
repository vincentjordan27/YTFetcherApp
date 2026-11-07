export type PendingChannelSortField = 'name' | 'subscriberCount' | 'discoveredAt' | 'evidenceCount'

export type PendingChannelSortDirection = 'asc' | 'desc'

export interface PendingChannelSort {
  readonly field: PendingChannelSortField
  readonly direction: PendingChannelSortDirection
}

export const DEFAULT_PENDING_CHANNEL_SORT: PendingChannelSort = { field: 'discoveredAt', direction: 'desc' }
