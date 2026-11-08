import type { RouteLocationRaw } from 'vue-router'

export type ChannelReviewVariant = 'pending' | 'approved' | 'rejected'
export type ChannelDialogKind = 'approve' | 'reject'
export type ChannelBackfillTone = 'completed' | 'running' | 'pending' | 'failed' | 'scheduling' | 'schedulingFailed'

export type ChannelTableState = 'ready' | 'loading' | 'empty' | 'error'

export interface ChannelTab {
  readonly id: ChannelReviewVariant
  readonly label: string
  readonly to: RouteLocationRaw
  readonly badge?: string
  readonly tone: 'warning' | 'active' | 'danger'
}

export interface ChannelEvidenceView {
  readonly id: string
  readonly title: string
  readonly thumbnailSrc: string | null
  readonly publishedAt: string
  readonly keyword: string
  readonly youtubeUrl: string
}

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
  readonly failureCount?: number
  readonly failureReason?: string
  readonly failureAt?: string
  readonly schedulingReason?: string
  readonly schedulingAt?: string
  readonly evidenceCount?: number
  readonly subscriberCount?: number | null
  readonly reasonTitle?: string
  readonly hasRejectionReason?: boolean
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

export type ChannelLedgerVariant = 'approved' | 'rejected'
