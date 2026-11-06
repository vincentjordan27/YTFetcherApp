export interface ApprovedChannelsRequest {
  readonly page: number
  readonly pageSize: number
  readonly query?: string
}

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
  readonly discoveredAt: string
  readonly approvedAt: string | null
  readonly backfillStatus: string
  readonly lastUploadCheckAt: string | null
  readonly backfillFailureCount: number
  readonly backfillFailureReason: string | null
  readonly backfillLastFailedAt: string | null
  readonly backfillSchedulingAttemptedAt: string | null
  readonly backfillSchedulingFailureReason: string | null
}
