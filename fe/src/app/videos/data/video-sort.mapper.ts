import type { VideoSort, VideoSortField } from '@/app/videos/types/video-sort.types'

/** Column headings as they appear in the sortable table header. */
const SORT_FIELD_LABELS: Readonly<Record<VideoSortField, string>> = {
  title: 'Video title & Channel',
  views: 'Current views',
  likes: 'Likes',
  publishedAt: 'Published',
  lastUpdatedAt: 'Last update',
  referenceSetAt: 'Reference date',
  referenceStatus: 'Status',
}

/** The header's accessible name, so the button reads as the column it sorts. */
export function toVideoSortAriaLabel(field: VideoSortField): string {
  return `Sort by ${SORT_FIELD_LABELS[field]}`
}

/**
 * The table's own toggle convention: activating a new column starts at ascending, activating the column
 * that is already active flips its direction.
 */
export function nextVideoSort(current: VideoSort, field: VideoSortField): VideoSort {
  if (current.field !== field) {
    return { field, direction: 'asc' }
  }

  return { field, direction: current.direction === 'asc' ? 'desc' : 'asc' }
}

/** `aria-sort` for one header cell. Every other column stays `none`. */
export function toAriaSort(current: VideoSort, field: VideoSortField): 'ascending' | 'descending' | 'none' {
  if (current.field !== field) {
    return 'none'
  }

  return current.direction === 'asc' ? 'ascending' : 'descending'
}
