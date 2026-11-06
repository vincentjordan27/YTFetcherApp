export interface RejectedChannelsRequest {
  readonly page: number
  readonly pageSize: number
  readonly query?: string
}

export interface RejectedChannelsListBody {
  readonly page: number
  readonly pageSize: number
  readonly searchText: string | null
}

export interface RejectedChannelsResponse {
  readonly items: readonly RejectedChannelItem[]
  readonly page: number
  readonly pageSize: number
  readonly totalCount: number
}

export interface RejectedChannelItem {
  readonly id: string
  readonly youtubeChannelId: string
  readonly name: string
  readonly thumbnailUrl: string | null
  readonly discoveredAt: string
  readonly rejectedAt: string | null
  readonly rejectionReason: string | null
}
