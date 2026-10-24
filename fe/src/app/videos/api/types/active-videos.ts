/** Wire contracts for the active-video list action `POST /api/videos/active/list`. */

/** Sort columns the active list accepts; mirrors the server contract exactly. */
export type ActiveVideosSortBy = 'title' | 'views' | 'likes' | 'publishedAt' | 'lastUpdatedAt'

export type ActiveVideosSortDirection = 'asc' | 'desc'

/** Caller-facing paging and sort input for the endpoint function. */
export interface ActiveVideosRequest {
  readonly page: number
  readonly pageSize: number
  readonly query?: string
  readonly sortBy: ActiveVideosSortBy
  readonly sortDirection: ActiveVideosSortDirection
}

/** JSON body of the paged list action. */
export interface ActiveVideosListBody {
  readonly page: number
  readonly pageSize: number
  readonly searchText: string | null
  readonly sortBy: ActiveVideosSortBy
  readonly sortDirection: ActiveVideosSortDirection
}

export interface ActiveVideosResponse {
  readonly items: readonly ActiveVideoItem[]
  readonly page: number
  readonly pageSize: number
  readonly totalCount: number
}

export interface ActiveVideoItem {
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
}
