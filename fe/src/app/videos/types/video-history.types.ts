/** Presentation state of the on-demand video history dialog. */
export type VideoHistoryState = 'ready' | 'loading' | 'empty' | 'error'

/** The row the operator asked history for; context comes from the already-rendered table row. */
export interface VideoHistoryTarget {
  readonly videoId: string
  readonly title: string
  readonly channelName: string
}

/** One captured statistics point, already formatted for display. */
export interface VideoHistoryRow {
  readonly id: string
  readonly capturedAtLabel: string
  readonly viewsLabel: string
  readonly likesLabel: string
}

export interface VideoHistoryPagination {
  readonly summary: string
  readonly pageCount: number
  readonly displayPages: readonly (number | 'ellipsis')[]
}
