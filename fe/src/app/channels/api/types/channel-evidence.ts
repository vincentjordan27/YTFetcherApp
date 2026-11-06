export interface ChannelEvidenceResponse {
  readonly items: readonly ChannelEvidenceItem[]
}

export interface ChannelEvidenceItem {
  readonly youtubeVideoId: string
  readonly title: string
  readonly thumbnailUrl: string | null
  readonly publishedAt: string
  readonly keyword: string
}
