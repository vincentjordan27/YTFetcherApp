export interface RejectChannelRequest {
  readonly reason: string
}

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
  readonly backfillSchedulingAttemptedAt: string | null
  readonly backfillSchedulingFailureReason: string | null
}
