/** Wire contracts for the backfill run history endpoints. */

/** Caller-facing input for the list endpoint function. */
export interface BackfillRunsRequest {
  readonly page: number
  readonly pageSize: number
  readonly query?: string
  readonly status?: string
  readonly triggerSource?: string
  readonly dateFrom?: string
  readonly dateTo?: string
}

/** JSON body of the paged list action. Every filter except paging is optional. */
export interface BackfillRunsListBody {
  readonly page: number
  readonly pageSize: number
  readonly searchText: string | null
  readonly status: string | null
  readonly triggerSource: string | null
  readonly dateFrom: string | null
  readonly dateTo: string | null
}

export interface BackfillRunsResponse {
  readonly items: readonly BackfillRunItem[]
  readonly page: number
  readonly pageSize: number
  readonly totalCount: number
}

/** One run row joined with its channel. Timestamps stay ISO/UTC on the wire. */
export interface BackfillRunItem {
  readonly id: string
  readonly channelId: string
  readonly channelName: string
  readonly youtubeChannelId: string
  readonly channelThumbnailUrl: string | null
  readonly pageNumber: number
  readonly triggerSource: string
  readonly status: string
  /** ISO 8601 timestamp with an offset. */
  readonly startedAt: string
  /** ISO 8601 timestamp with an offset, or null while the run is still open. */
  readonly completedAt: string | null
  /** Readable operator-facing reason, present only for a failed run. */
  readonly failureReason: string | null
  readonly failureCountAtTime: number | null
  /**
   * Provider bucket that reached its limit for a `QUOTA_LIMIT_REACHED` run: `QUERIES` or
   * `VIDEO_BATCH_STATS`; null for every other status.
   */
  readonly quotaLimitBucket: string | null
  /**
   * Server decision: this run is retryable. The client never derives it, because only the server can see
   * every run of the channel and the channel's current page-work state.
   */
  readonly canRetry: boolean
}

/** Readable field errors of a `400 application/problem+json` response. */
export interface BackfillRunValidationProblem {
  readonly errors?: Readonly<Record<string, readonly string[]>>
}

/** Readable message of a `404` or `409 application/problem+json` response. */
export interface BackfillRunProblem {
  readonly detail?: string
}
