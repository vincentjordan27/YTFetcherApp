import type { VideoHistoryItem } from '@/app/videos/api/types/video-history'
import type { VideoHistoryRow } from '@/app/videos/types/video-history.types'

/** Shown when the capture timestamp is not a usable date. */
const EMPTY_DISPLAY = '—'

const dateTimeFormatter = new Intl.DateTimeFormat('en-GB', {
  day: '2-digit',
  month: 'short',
  year: 'numeric',
  hour: '2-digit',
  minute: '2-digit',
  hour12: false,
})

const numberFormatter = new Intl.NumberFormat('en-US')

/** Maps one API snapshot to a display row in the browser's local timezone. */
export function toVideoHistoryRow(item: VideoHistoryItem, index: number): VideoHistoryRow {
  const capturedAt = new Date(item.capturedAt)

  return {
    // A snapshot has no server id in this contract, and equal capture instants are possible, so the page
    // position keeps the key unique within one rendered page.
    id: `${item.capturedAt}-${index}`,
    capturedAtLabel: Number.isNaN(capturedAt.getTime())
      ? EMPTY_DISPLAY
      : dateTimeFormatter.format(capturedAt),
    viewsLabel: numberFormatter.format(item.viewCount),
    likesLabel: numberFormatter.format(item.likeCount),
  }
}
