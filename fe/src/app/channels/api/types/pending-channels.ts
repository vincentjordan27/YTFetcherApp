/** Wire contracts for the pending list action `POST /api/channels/pending/list`. */

/** Caller-facing paging input for the endpoint function. */
export interface PendingChannelsRequest {
  readonly page: number
  readonly pageSize: number
  readonly query?: string
}

/** JSON body of the paged list action. Filters and sorting can grow here without changing the route. */
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
  /** ISO 8601 timestamp with an offset. */
  readonly discoveredAt: string
  readonly evidenceCount: number
}
