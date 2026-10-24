/** Presentation state of the active-video table while data is requested from the API. */
export type ActiveVideoTableState = 'ready' | 'loading' | 'empty' | 'error'

/**
 * One active-video row. Every value is already formatted for display; the API returns raw values and
 * duration, compact numbers, age, and dates are formatted in the browser.
 */
export interface ActiveVideoRow {
  readonly id: string
  readonly youtubeVideoId: string
  readonly title: string
  readonly channelName: string
  /** Provider channel id; the channel link is built from this, never from the display name. */
  readonly youtubeChannelId: string
  readonly thumbnailSrc: string | null
  readonly durationLabel: string
  readonly viewsLabel: string
  readonly likesLabel: string
  readonly publishedLabel: string
  readonly ageLabel: string
  readonly lastStatsFetchedLabel: string
}

export interface ActiveVideoPagination {
  readonly summary: string
  readonly pageCount: number
  readonly displayPages: readonly (number | 'ellipsis')[]
}
