import { apiRequest } from '@/common/api/api-client'
import type { ActiveVideoCountResponse } from '@/app/videos/api/types/active-video-count'
import type {
  ActiveVideosListBody,
  ActiveVideosRequest,
  ActiveVideosResponse,
} from '@/app/videos/api/types/active-videos'
import type { MarkVideosReferenceBody } from '@/app/videos/api/types/mark-videos-reference'
import type { UnmarkVideoReferenceBody } from '@/app/videos/api/types/unmark-video-reference'
import type { SetVideoReferenceUsedBody } from '@/app/videos/api/types/set-video-reference-used'
import type {
  ReferenceVideosListBody,
  ReferenceVideosRequest,
  ReferenceVideosResponse,
} from '@/app/videos/api/types/reference-videos'
import type {
  VideoHistoryListBody,
  VideoHistoryRequest,
  VideoHistoryResponse,
} from '@/app/videos/api/types/video-history'

export function getActiveVideos(request: ActiveVideosRequest): Promise<ActiveVideosResponse> {
  const body: ActiveVideosListBody = {
    page: request.page,
    pageSize: request.pageSize,
    searchText: request.query ?? null,
    sortBy: request.sortBy,
    sortDirection: request.sortDirection,
  }

  return apiRequest<ActiveVideosResponse>('/api/videos/active/list', {
    method: 'POST',
    body: JSON.stringify(body),
  })
}

export function getReferenceVideos(request: ReferenceVideosRequest): Promise<ReferenceVideosResponse> {
  const body: ReferenceVideosListBody = {
    page: request.page,
    pageSize: request.pageSize,
    searchText: request.query ?? null,
    sortBy: request.sortBy,
    sortDirection: request.sortDirection,
  }

  return apiRequest<ReferenceVideosResponse>('/api/videos/reference/list', {
    method: 'POST',
    body: JSON.stringify(body),
  })
}

export function getActiveVideoCount(): Promise<ActiveVideoCountResponse> {
  return apiRequest<ActiveVideoCountResponse>('/api/videos/active/count')
}

/** All-or-nothing: the server returns 204 or a readable 409 conflict. */
export function markVideosReference(videoIds: readonly string[]): Promise<void> {
  const body: MarkVideosReferenceBody = { videoIds }

  return apiRequest<void>('/api/videos/mark-reference', {
    method: 'POST',
    body: JSON.stringify(body),
  })
}

/** Per-row removal from Reference. 204 on success, a readable 404 when the row is gone. */
export function unmarkVideoReference(videoId: string): Promise<void> {
  const body: UnmarkVideoReferenceBody = { videoId }

  return apiRequest<void>('/api/videos/unmark-reference', {
    method: 'POST',
    body: JSON.stringify(body),
  })
}

/** Sets the current Used/Unused flag without removing the video from Reference. */
export function setReferenceVideoUsed(videoId: string, isUsed: boolean): Promise<void> {
  const body: SetVideoReferenceUsedBody = { videoId, isUsed }

  return apiRequest<void>('/api/videos/reference/set-used', {
    method: 'POST',
    body: JSON.stringify(body),
  })
}

/** On-demand snapshot history for exactly one video; never part of a list request. */
export function getVideoHistory(
  videoId: string,
  request: VideoHistoryRequest,
): Promise<VideoHistoryResponse> {
  const body: VideoHistoryListBody = {
    page: request.page,
    pageSize: request.pageSize,
  }

  return apiRequest<VideoHistoryResponse>(`/api/videos/${videoId}/history/list`, {
    method: 'POST',
    body: JSON.stringify(body),
  })
}
