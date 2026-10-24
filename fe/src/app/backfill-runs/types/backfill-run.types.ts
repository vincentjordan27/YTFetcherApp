/** One run-history row as the approved table renders it. */
export interface BackfillRunRow {
  id: string
  channelId: string
  channelName: string
  youtubeChannelId: string
  thumbnailSrc: string | null
  pageNumber: number
  triggerLabel: string
  statusLabel: string
  statusTone: BackfillRunStatusTone
  /** Already localized for display. */
  startedLabel: string
  /** Already localized for display, or an em dash while the run is open. */
  finishedLabel: string
  /** Readable reason, or an em dash when the run did not fail. */
  errorLabel: string
  failureCountAtTime: number | null
  /** The provider bucket that reached its limit, or null. */
  quotaLimitBucketLabel: string | null
  /** True when this row is the channel's newest run and that run is terminally failed. */
  canRetry: boolean
}

export type BackfillRunStatusTone = 'running' | 'succeeded' | 'failed' | 'quota' | 'abandoned'

export interface BackfillRunPagination {
  readonly summary: string
  readonly pageCount: number
  readonly displayPages: readonly (number | 'ellipsis')[]
}

export type BackfillRunTableState =
  | { readonly state: 'loading'; readonly message: string }
  | { readonly state: 'error'; readonly message: string }
  | { readonly state: 'empty'; readonly message: string }
  | { readonly state: 'ready'; readonly message: '' }

export type BackfillRunDialogKind = 'detail' | 'retry' | null
