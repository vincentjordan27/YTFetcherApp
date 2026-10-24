/** Wire contracts for `GET /api/channels/{channelId}/evidence`. */

export interface ChannelEvidenceResponse {
  readonly items: readonly ChannelEvidenceItem[]
}

export interface ChannelEvidenceItem {
  readonly youtubeVideoId: string
  readonly title: string
  readonly thumbnailUrl: string | null
  /** ISO 8601 timestamp with an offset. */
  readonly publishedAt: string
  readonly keyword: string
}
