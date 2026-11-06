export interface PendingChannelsRequest {
  readonly page: number
  readonly pageSize: number
  readonly query?: string
}

export interface PendingChannelsListBody {
  readonly page: number
  readonly pageSize: number
  readonly searchText: string | null
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
