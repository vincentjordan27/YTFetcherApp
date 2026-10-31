/** Wire contracts for the reference-video list action `POST /api/videos/reference/list`. */

/** Sort columns the reference list accepts; mirrors the server contract exactly. */
export type ReferenceVideosSortBy = 'title' | 'views' | 'likes' | 'publishedAt' | 'lastUpdatedAt' | 'referenceSetAt'

export type ReferenceVideosSortDirection = 'asc' | 'desc'

/** Caller-facing paging and sort input for the endpoint function. */
export interface ReferenceVideosRequest {
  readonly page: number
  readonly pageSize: number
  readonly query?: string
  readonly sortBy: ReferenceVideosSortBy
  readonly sortDirection: ReferenceVideosSortDirection
}

/** JSON body of the paged list action. */
export interface ReferenceVideosListBody {
  readonly page: number
  readonly pageSize: number
  readonly searchText: string | null
  readonly sortBy: ReferenceVideosSortBy
  readonly sortDirection: ReferenceVideosSortDirection
}

export interface ReferenceVideosResponse {
  readonly items: readonly ReferenceVideoItem[]
  readonly page: number
  readonly pageSize: number
  readonly totalCount: number
}

export interface ReferenceVideoItem {
  readonly id: string
  readonly youtubeVideoId: string
  readonly title: string
  readonly channelName: string
  /** Provider channel id, used to build the channel link; never inferred from the display name. */
  readonly youtubeChannelId: string
  readonly thumbnailUrl: string | null
  readonly durationMillis: number
  readonly currentViews: number
  readonly currentLikes: number
  /** ISO 8601 timestamp with an offset. */
  readonly publishedAt: string
  /** ISO 8601 timestamp with an offset, or null. */
  readonly lastStatsFetchedAt: string | null
  readonly isReferenceUsed: boolean
  readonly referenceSetAt: string | null
}
