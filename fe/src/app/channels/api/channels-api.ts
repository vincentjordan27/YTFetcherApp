import { apiRequest } from '@/common/api/api-client'
import type { ChannelEvidenceResponse } from '@/app/channels/api/types/channel-evidence'
import type { ChannelDetailResponse } from '@/app/channels/api/types/channel-review-mutation'
import type { ManualApproveChannelBody } from '@/app/channels/api/types/manual-approve-channel'
import type {
  ApprovedChannelsListBody,
  ApprovedChannelsRequest,
  ApprovedChannelsResponse,
} from '@/app/channels/api/types/approved-channels'
import type {
  PendingChannelsListBody,
  PendingChannelsRequest,
  PendingChannelsResponse,
} from '@/app/channels/api/types/pending-channels'
import type {
  RejectedChannelsListBody,
  RejectedChannelsRequest,
  RejectedChannelsResponse,
} from '@/app/channels/api/types/rejected-channels'
import type { ReviewCountsResponse } from '@/app/channels/api/types/review-counts'
import type { BulkApproveChannelsBody } from '@/app/channels/api/types/bulk-approve-channels'
import type { BulkRejectChannelsBody } from '@/app/channels/api/types/bulk-reject-channels'

export function getPendingChannels(request: PendingChannelsRequest): Promise<PendingChannelsResponse> {
  const body: PendingChannelsListBody = {
    page: request.page,
    pageSize: request.pageSize,
    searchText: request.query ?? null,
  }

  return apiRequest<PendingChannelsResponse>('/api/channels/pending/list', {
    method: 'POST',
    body: JSON.stringify(body),
  })
}

export function getApprovedChannels(request: ApprovedChannelsRequest): Promise<ApprovedChannelsResponse> {
  const body: ApprovedChannelsListBody = {
    page: request.page,
    pageSize: request.pageSize,
    searchText: request.query ?? null,
  }

  return apiRequest<ApprovedChannelsResponse>('/api/channels/approved/list', {
    method: 'POST',
    body: JSON.stringify(body),
  })
}

export function getRejectedChannels(request: RejectedChannelsRequest): Promise<RejectedChannelsResponse> {
  const body: RejectedChannelsListBody = {
    page: request.page,
    pageSize: request.pageSize,
    searchText: request.query ?? null,
  }

  return apiRequest<RejectedChannelsResponse>('/api/channels/rejected/list', {
    method: 'POST',
    body: JSON.stringify(body),
  })
}

export function getReviewCounts(): Promise<ReviewCountsResponse> {
  return apiRequest<ReviewCountsResponse>('/api/channels/review-counts')
}

export function getChannelEvidence(channelId: string): Promise<ChannelEvidenceResponse> {
  return apiRequest<ChannelEvidenceResponse>(`/api/channels/${channelId}/evidence`)
}

export function approveChannel(channelId: string): Promise<ChannelDetailResponse> {
  // Approve carries no data yet, but every non-GET action sends a JSON body.
  return apiRequest<ChannelDetailResponse>(`/api/channels/${channelId}/approve`, {
    method: 'POST',
    body: JSON.stringify({}),
  })
}

/** Every selected channel is approved, or the API changes none of them. */
export function approveChannels(channelIds: readonly string[]): Promise<void> {
  const body: BulkApproveChannelsBody = { channelIds }

  return apiRequest<void>('/api/channels/approve', {
    method: 'POST',
    body: JSON.stringify(body),
  })
}

export function rejectChannel(channelId: string, reason: string): Promise<ChannelDetailResponse> {
  return apiRequest<ChannelDetailResponse>(`/api/channels/${channelId}/reject`, {
    method: 'POST',
    body: JSON.stringify({ reason }),
  })
}

/** Every selected channel receives the same reason, or the API changes none of them. */
export function rejectChannels(channelIds: readonly string[], reason: string): Promise<void> {
  const body: BulkRejectChannelsBody = { channelIds, reason }

  return apiRequest<void>('/api/channels/reject', {
    method: 'POST',
    body: JSON.stringify(body),
  })
}

export function reopenChannel(channelId: string): Promise<ChannelDetailResponse> {
  return apiRequest<ChannelDetailResponse>(`/api/channels/${channelId}/reopen`, {
    method: 'POST',
    body: JSON.stringify({}),
  })
}

/** Adds one channel found outside discovery by its @handle; the server validates it with YouTube. */
export function manualApproveChannel(channelReference: string): Promise<ChannelDetailResponse> {
  const body: ManualApproveChannelBody = { channelReference }

  return apiRequest<ChannelDetailResponse>('/api/channels/manual-approve', {
    method: 'POST',
    body: JSON.stringify(body),
  })
}
