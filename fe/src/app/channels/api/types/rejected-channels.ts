/** Wire contracts for the rejected list action `POST /api/channels/rejected/list`. */

/** Caller-facing paging input for the endpoint function. */
export interface RejectedChannelsRequest {
  readonly page: number
  readonly pageSize: number
  readonly query?: string
}

/** JSON body of the paged list action. */
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
  /** ISO 8601 timestamp with an offset. */
  readonly discoveredAt: string
  /** ISO 8601 timestamp with an offset, or null. */
  readonly rejectedAt: string | null
  readonly rejectionReason: string | null
}
