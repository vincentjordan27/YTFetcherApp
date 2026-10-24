import type { BackfillRunItem } from '@/app/backfill-runs/api/types/backfill-runs'
import type { BackfillRunRow, BackfillRunStatusTone } from '@/app/backfill-runs/types/backfill-run.types'

/** Shown when a nullable API value has no data yet. */
export const EMPTY_DISPLAY = '\u2014'

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

const STATUS_LABELS: Readonly<Record<string, { label: string; tone: BackfillRunStatusTone }>> = {
  RUNNING: { label: 'Running', tone: 'running' },
  SUCCEEDED: { label: 'Succeeded', tone: 'succeeded' },
  FAILED: { label: 'Failed', tone: 'failed' },
  // An expected provider limit is a distinct status, never rendered as a generic failure.
  QUOTA_LIMIT_REACHED: { label: 'Quota limit reached', tone: 'quota' },
  ABANDONED: { label: 'Abandoned', tone: 'abandoned' },
}

const BUCKET_LABELS: Readonly<Record<string, string>> = {
  QUERIES: 'Queries',
  VIDEO_BATCH_STATS: 'Video Batch Stats',
}

const TRIGGER_LABELS: Readonly<Record<string, string>> = {
  APPROVAL: 'Approval',
  DAILY_PUMP: 'Daily pump',
  RECOVERY: 'Recovery',
}

/** Formats an API timestamp in the viewer's timezone, for example `05 Oct 2026, 14:30`. */
export function formatBackfillRunDateTime(value: string): string {
  const date = new Date(value)
  return Number.isNaN(date.getTime())
    ? EMPTY_DISPLAY
    : `${dateFormatter.format(date)}, ${timeFormatter.format(date)}`
}

function formatNullable(value: string | null): string {
  return value === null ? EMPTY_DISPLAY : formatBackfillRunDateTime(value)
}

/**
 * Maps one API run to a ledger row. Retry visibility comes straight from the server's `canRetry`, so no
 * client-side guess about newer runs is involved.
 */
export function toBackfillRunRow(item: BackfillRunItem): BackfillRunRow {
  const status = STATUS_LABELS[item.status] ?? { label: item.status, tone: 'abandoned' as BackfillRunStatusTone }

  return {
    id: item.id,
    channelId: item.channelId,
    channelName: item.channelName,
    youtubeChannelId: item.youtubeChannelId,
    thumbnailSrc: item.channelThumbnailUrl,
    pageNumber: item.pageNumber,
    triggerLabel: TRIGGER_LABELS[item.triggerSource] ?? item.triggerSource,
    statusLabel: status.label,
    statusTone: status.tone,
    startedLabel: formatBackfillRunDateTime(item.startedAt),
    finishedLabel: formatNullable(item.completedAt),
    errorLabel: item.status === 'QUOTA_LIMIT_REACHED'
      ? `Quota limit reached: ${bucketLabel(item.quotaLimitBucket)}`
      : item.failureReason ?? EMPTY_DISPLAY,
    failureCountAtTime: item.failureCountAtTime,
    quotaLimitBucketLabel: item.quotaLimitBucket === null ? null : bucketLabel(item.quotaLimitBucket),
    canRetry: item.canRetry,
  }
}

function bucketLabel(bucket: string | null): string {
  return bucket === null ? EMPTY_DISPLAY : BUCKET_LABELS[bucket] ?? bucket
}
