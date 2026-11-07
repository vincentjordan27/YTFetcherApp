import type { PendingChannelSortDirection, PendingChannelSortField } from '@/app/channels/types/pending-channel-sort'

export interface PendingChannelsRequest {
  readonly page: number
  readonly pageSize: number
  readonly query?: string
  readonly sortBy: PendingChannelSortField
  readonly sortDirection: PendingChannelSortDirection
}

export interface PendingChannelsListBody {
  readonly page: number
  readonly pageSize: number
  readonly searchText: string | null
  readonly sortBy: string
  readonly sortDirection: string
}

export interface PendingChannelsResponse {
  readonly items: readonly PendingChannelItem[]
  readonly page: number
  readonly pageSize: number
  readonly totalCount: number
}

export interface PendingChannelItem {
  readonly id: string
  readonly youtubeChannelId: string
  readonly name: string
  readonly thumbnailUrl: string | null
  readonly discoveredAt: string
  readonly evidenceCount: number
  readonly subscriberCount: number | null
}
