/** Wire contracts for the approved list action `POST /api/channels/approved/list`. */

/** Caller-facing paging input for the endpoint function. */
export interface ApprovedChannelsRequest {
  readonly page: number
  readonly pageSize: number
  readonly query?: string
}

/** JSON body of the paged list action. */
export interface ApprovedChannelsListBody {
  readonly page: number
  readonly pageSize: number
  readonly searchText: string | null
}

export interface ApprovedChannelsResponse {
  readonly items: readonly ApprovedChannelItem[]
  readonly page: number
  readonly pageSize: number
  readonly totalCount: number
}

export interface ApprovedChannelItem {
  readonly id: string
  readonly youtubeChannelId: string
  readonly name: string
  readonly thumbnailUrl: string | null
  /** ISO 8601 timestamp with an offset. */
  readonly discoveredAt: string
  /** ISO 8601 timestamp with an offset, or null. */
  readonly approvedAt: string | null
  /** BackfillStatus: PENDING, RUNNING, COMPLETED, FAILED, SCHEDULING, or SCHEDULING_FAILED. */
  readonly backfillStatus: string
  /** ISO 8601 timestamp with an offset, or null until upload monitoring runs. */
  readonly lastUploadCheckAt: string | null
  /** Consecutive backfill failures, 0 to 3. Non-zero only while the backfill status is FAILED. */
  readonly backfillFailureCount: number
  /** Safe operator-facing failure summary, or null. Never raw provider or database text. */
  readonly backfillFailureReason: string | null
  /** ISO 8601 timestamp with an offset, or null. */
  readonly backfillLastFailedAt: string | null
  /**
   * ISO 8601 timestamp with an offset. Non-null only for SCHEDULING or SCHEDULING_FAILED; queue
   * scheduling only, never a backfill execution failure.
   */
  readonly backfillSchedulingAttemptedAt: string | null
  /** Safe operator-facing scheduling failure summary, or null. Never raw exception text. */
  readonly backfillSchedulingFailureReason: string | null
}
