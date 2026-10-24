/** Wire contracts for the approve and reject mutations. */

export interface RejectChannelRequest {
  readonly reason: string
}

/** Shared channel detail response returned by approve and reject. */
export interface ChannelDetailResponse {
  readonly id: string
  readonly youtubeChannelId: string
  readonly name: string
  readonly thumbnailUrl: string | null
  readonly status: string
  readonly uploadsPlaylistId: string | null
  readonly backfillStatus: string
  readonly discoveredAt: string
  readonly approvedAt: string | null
  readonly rejectedAt: string | null
  readonly lastUploadCheckAt: string | null
  readonly createdAt: string
  readonly modifiedAt: string
  /** Non-null only for SCHEDULING or SCHEDULING_FAILED. */
  readonly backfillSchedulingAttemptedAt: string | null
  /** Safe operator-facing scheduling failure summary, or null. */
  readonly backfillSchedulingFailureReason: string | null
}
