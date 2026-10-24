import type { RouteLocationRaw } from 'vue-router'

export type ChannelReviewVariant = 'pending' | 'approved' | 'rejected'
export type ChannelDialogKind = 'approve' | 'reject'
export type ChannelBackfillTone = 'completed' | 'running' | 'pending' | 'failed' | 'scheduling' | 'schedulingFailed'

/** Presentation state of the channel ledger while data is requested from the API. */
export type ChannelTableState = 'ready' | 'loading' | 'empty' | 'error'

export interface ChannelTab {
  readonly id: ChannelReviewVariant
  readonly label: string
  readonly to: RouteLocationRaw
  /** Omitted while the review counts have never been loaded successfully, so unknown is not shown as 0. */
  readonly badge?: string
  readonly tone: 'warning' | 'active' | 'danger'
}

/**
 * One discovery evidence row. It is provenance for review, not a tracked video record, so it has no
 * duration or statistics.
 */
export interface ChannelEvidenceView {
  readonly id: string
  readonly title: string
  readonly thumbnailSrc: string | null
  readonly publishedAt: string
  readonly keyword: string
  readonly youtubeUrl: string
}

/** Evidence data needed to render every expanded row of the ledger. */
export interface ChannelEvidenceState {
  readonly items: Readonly<Record<string, readonly ChannelEvidenceView[]>>
  readonly loadingId: string | null
  readonly failed: Readonly<Record<string, boolean>>
}

export interface ChannelReviewRow {
  readonly id: string
  readonly name: string
  readonly handle: string
  readonly avatarSrc: string | null
  readonly youtubeUrl: string
  readonly statusLabel: string
  readonly discoveredAt: string
  readonly approvedAt?: string
  readonly rejectedAt?: string
  readonly backfillLabel?: string
  readonly backfillTone?: ChannelBackfillTone
  readonly lastUploadCheck?: string
  /** Persisted consecutive backfill failures, 1 to 3. */
  readonly failureCount?: number
  /** Safe operator-facing last failure summary written by the API. */
  readonly failureReason?: string
  readonly failureAt?: string
  /** Safe operator-facing scheduling failure summary, set only for SCHEDULING_FAILED. */
  readonly schedulingReason?: string
  /** Persisted time of the last queue-enqueue attempt, set only for SCHEDULING states. */
  readonly schedulingAt?: string
  readonly evidenceCount?: number
  readonly reasonTitle?: string
}

export interface ChannelPaginationDesign {
  readonly summary: string
  readonly pageCount: number
  readonly displayPages: readonly (number | 'ellipsis')[]
}

export interface ChannelDialogTarget {
  readonly id: string
  readonly name: string
  readonly handle: string
}

/** Narrow ledger variant owned by the shared approved/rejected ledger composable. */
export type ChannelLedgerVariant = 'approved' | 'rejected'
