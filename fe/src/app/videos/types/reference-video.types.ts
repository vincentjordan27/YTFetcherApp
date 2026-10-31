/** Presentation state of the reference-video table while data is requested from the API. */
export type ReferenceVideoTableState = 'ready' | 'loading' | 'empty' | 'error'

/**
 * One reference-video row. Every value is already formatted for display; the API returns raw values
 * and duration, compact numbers, age, and dates are formatted in the browser.
 */
export interface ReferenceVideoRow {
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
  readonly referenceSetLabel: string
  readonly isReferenceUsed: boolean
}

export interface ReferenceVideoPagination {
  readonly summary: string
  readonly pageCount: number
  readonly displayPages: readonly (number | 'ellipsis')[]
}
