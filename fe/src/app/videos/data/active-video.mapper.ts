import type { ActiveVideoItem } from '@/app/videos/api/types/active-videos'
import type { ActiveVideoRow } from '@/app/videos/types/active-video.types'

/** Shown when a nullable API value has no data. */
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

const compactNumberFormatter = new Intl.NumberFormat('en-US', {
  notation: 'compact',
  maximumFractionDigits: 1,
})

/** Maps one API video to a display row. All formatting happens here, never on the server. */
export function toActiveVideoRow(item: ActiveVideoItem): ActiveVideoRow {
  return {
    id: item.id,
    youtubeVideoId: item.youtubeVideoId,
    title: item.title,
    channelName: item.channelName,
    youtubeChannelId: item.youtubeChannelId,
    thumbnailSrc: item.thumbnailUrl,
    durationLabel: formatDuration(item.durationMillis),
    viewsLabel: compactNumberFormatter.format(item.currentViews),
    likesLabel: compactNumberFormatter.format(item.currentLikes),
    publishedLabel: formatDate(item.publishedAt),
    ageLabel: formatAge(item.publishedAt),
    lastStatsFetchedLabel: formatDateTime(item.lastStatsFetchedAt),
  }
}

/** Every Stage 3 video is short form, so the duration is always rendered as `m:ss`. */
function formatDuration(durationMillis: number): string {
  if (!Number.isFinite(durationMillis) || durationMillis < 0) {
    return EMPTY_DISPLAY
  }

  const totalSeconds = Math.floor(durationMillis / 1000)
  const minutes = Math.floor(totalSeconds / 60)
  const seconds = totalSeconds % 60

  return `${minutes}:${String(seconds).padStart(2, '0')}`
}

function formatDate(value: string): string {
  const date = new Date(value)
  return Number.isNaN(date.getTime()) ? EMPTY_DISPLAY : dateFormatter.format(date)
}

function formatDateTime(value: string | null): string {
  const date = value === null ? null : new Date(value)

  return date === null || Number.isNaN(date.getTime())
    ? EMPTY_DISPLAY
    : `${dateFormatter.format(date)}, ${timeFormatter.format(date)}`
}

function formatAge(value: string): string {
  const published = new Date(value)
  if (Number.isNaN(published.getTime())) {
    return EMPTY_DISPLAY
  }

  const days = Math.max(0, Math.floor((Date.now() - published.getTime()) / 86_400_000))

  if (days === 0) return 'Today'
  return days === 1 ? '1 day old' : `${days} days old`
}
