/**
 * Server-side sort contract shared by the Active and Reference video lists. Both routes accept exactly these
 * fields and directions; nothing here sorts rows in the browser.
 */
export type VideoSortField = 'title' | 'views' | 'likes' | 'publishedAt' | 'lastUpdatedAt' | 'referenceSetAt'

export type VideoSortDirection = 'asc' | 'desc'

export interface VideoSort {
  readonly field: VideoSortField
  readonly direction: VideoSortDirection
}

/** Matches the historical list order and the toolbar wording, so an untouched page looks unchanged. */
export const DEFAULT_VIDEO_SORT: VideoSort = { field: 'views', direction: 'desc' }
