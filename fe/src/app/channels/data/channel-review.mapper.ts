import type { ApprovedChannelItem } from '@/app/channels/api/types/approved-channels'
import type { ChannelEvidenceItem } from '@/app/channels/api/types/channel-evidence'
import type { PendingChannelItem } from '@/app/channels/api/types/pending-channels'
import type { RejectedChannelItem } from '@/app/channels/api/types/rejected-channels'
import type { ChannelBackfillTone, ChannelEvidenceView, ChannelReviewRow } from '@/app/channels/types/channel-review'

/** Shown when a nullable API value has no data yet. */
export const EMPTY_DISPLAY = '—'

const dateFormatter = new Intl.DateTimeFormat('en-GB', {
  day: '2-digit',
  month: 'short',
  year: 'numeric',
})

const timeFormatter = new Intl.DateTimeFormat('en-GB', {
  hour: '2-digit',
  minute: '2-digit',
  hour12: false,
})

const BACKFILL_TONES: Readonly<Record<string, { label: string; tone: ChannelBackfillTone }>> = {
  PENDING: { label: 'Pending', tone: 'pending' },
  RUNNING: { label: 'Running', tone: 'running' },
  COMPLETED: { label: 'Completed', tone: 'completed' },
  FAILED: { label: 'Failed', tone: 'failed' },
  SCHEDULING: { label: 'Scheduling', tone: 'scheduling' },
  SCHEDULING_FAILED: { label: 'Scheduling failed', tone: 'schedulingFailed' },
}

/** Formats an API timestamp with the approved visual baseline shape, for example `16 Sep 2026, 14:30`. */
export function formatChannelDateTime(value: string): string {
  const date = new Date(value)
  if (Number.isNaN(date.getTime())) {
    return EMPTY_DISPLAY
  }

  return `${dateFormatter.format(date)}, ${timeFormatter.format(date)}`
}

function formatNullableChannelDateTime(value: string | null): string {
  return value === null ? EMPTY_DISPLAY : formatChannelDateTime(value)
}

function toYoutubeChannelUrl(youtubeChannelId: string): string {
  return `https://www.youtube.com/channel/${youtubeChannelId}`
}

/**
 * Maps a pending channel from the API to a ledger row. The real YouTube channel ID is the secondary
 * identity text, so no handle is fabricated.
 */
export function toPendingChannelRow(item: PendingChannelItem): ChannelReviewRow {
  return {
    id: item.id,
    name: item.name,
    handle: item.youtubeChannelId,
    avatarSrc: item.thumbnailUrl,
    youtubeUrl: toYoutubeChannelUrl(item.youtubeChannelId),
    statusLabel: 'Pending',
    discoveredAt: formatChannelDateTime(item.discoveredAt),
    backfillLabel: 'Pending',
    evidenceCount: item.evidenceCount,
  }
}

/** Maps an approved channel. Backfill labels are the persisted BackfillStatus values. */
export function toApprovedChannelRow(item: ApprovedChannelItem): ChannelReviewRow {
  const backfill = BACKFILL_TONES[item.backfillStatus] ?? { label: item.backfillStatus, tone: 'pending' }

  return {
    id: item.id,
    name: item.name,
    handle: item.youtubeChannelId,
    avatarSrc: item.thumbnailUrl,
    youtubeUrl: toYoutubeChannelUrl(item.youtubeChannelId),
    statusLabel: 'Approved',
    discoveredAt: formatChannelDateTime(item.discoveredAt),
    approvedAt: formatNullableChannelDateTime(item.approvedAt),
    backfillLabel: backfill.label,
    backfillTone: backfill.tone,
    lastUploadCheck: formatNullableChannelDateTime(item.lastUploadCheckAt),
    failureCount: item.backfillFailureCount,
    failureReason: item.backfillFailureReason ?? undefined,
    failureAt: formatNullableChannelDateTime(item.backfillLastFailedAt),
    schedulingReason: item.backfillSchedulingFailureReason ?? undefined,
    schedulingAt: formatNullableChannelDateTime(item.backfillSchedulingAttemptedAt),
  }
}

/** Maps a rejected channel. The rejection reason is the row title; there is no detail value. */
export function toRejectedChannelRow(item: RejectedChannelItem): ChannelReviewRow {
  return {
    id: item.id,
    name: item.name,
    handle: item.youtubeChannelId,
    avatarSrc: item.thumbnailUrl,
    youtubeUrl: toYoutubeChannelUrl(item.youtubeChannelId),
    statusLabel: 'Rejected',
    discoveredAt: formatChannelDateTime(item.discoveredAt),
    rejectedAt: formatNullableChannelDateTime(item.rejectedAt),
    reasonTitle: item.rejectionReason ?? EMPTY_DISPLAY,
  }
}

export function toChannelEvidenceView(item: ChannelEvidenceItem): ChannelEvidenceView {
  return {
    id: item.youtubeVideoId,
    title: item.title,
    thumbnailSrc: item.thumbnailUrl,
    publishedAt: formatChannelDateTime(item.publishedAt),
    keyword: item.keyword,
    youtubeUrl: `https://www.youtube.com/watch?v=${item.youtubeVideoId}`,
  }
}
