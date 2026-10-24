/** Wire contracts for the on-demand per-video history action `POST /api/videos/{videoId}/history/list`. */

/** Caller-facing paging input for the endpoint function. */
export interface VideoHistoryRequest {
  readonly page: number
  readonly pageSize: number
}

/** JSON body of the paged history action. */
export interface VideoHistoryListBody {
  readonly page: number
  readonly pageSize: number
}

export interface VideoHistoryResponse {
  readonly items: readonly VideoHistoryItem[]
  readonly page: number
  readonly pageSize: number
  readonly totalCount: number
}

export interface VideoHistoryItem {
  /** ISO 8601 timestamp with an offset. */
  readonly capturedAt: string
  readonly viewCount: number
  readonly likeCount: number
}
