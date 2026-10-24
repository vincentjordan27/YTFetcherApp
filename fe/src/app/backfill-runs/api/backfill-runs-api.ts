import { apiRequest } from '@/common/api/api-client'
import type {
  BackfillRunsListBody,
  BackfillRunsRequest,
  BackfillRunsResponse,
} from '@/app/backfill-runs/api/types/backfill-runs'

export function getBackfillRuns(request: BackfillRunsRequest): Promise<BackfillRunsResponse> {
  const body: BackfillRunsListBody = {
    page: request.page,
    pageSize: request.pageSize,
    searchText: request.query ?? null,
    status: request.status ?? null,
    triggerSource: request.triggerSource ?? null,
    dateFrom: request.dateFrom ?? null,
    dateTo: request.dateTo ?? null,
  }

  return apiRequest<BackfillRunsResponse>('/api/backfill-runs/list', {
    method: 'POST',
    body: JSON.stringify(body),
  })
}

/**
 * Retries terminally failed page work for one channel. Explicit body: the endpoint takes no input but
 * every non-GET action still sends JSON. The server decides eligibility.
 */
export function retryBackfill(channelId: string): Promise<unknown> {
  return apiRequest<unknown>(`/api/channels/${channelId}/backfill/retry`, {
    method: 'POST',
    body: JSON.stringify({}),
  })
}
