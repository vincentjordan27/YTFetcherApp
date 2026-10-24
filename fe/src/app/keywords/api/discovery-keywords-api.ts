import { apiRequest } from '@/common/api/api-client'
import type {
  CreateDiscoveryKeywordBody,
  DiscoveryKeywordItem,
  DiscoveryKeywordsListBody,
  DiscoveryKeywordsRequest,
  DiscoveryKeywordsResponse,
} from '@/app/keywords/api/types/discovery-keywords'

export function getDiscoveryKeywords(
  request: DiscoveryKeywordsRequest,
): Promise<DiscoveryKeywordsResponse> {
  const body: DiscoveryKeywordsListBody = {
    page: request.page,
    pageSize: request.pageSize,
    searchText: request.query ?? null,
  }

  return apiRequest<DiscoveryKeywordsResponse>('/api/discovery-keywords/list', {
    method: 'POST',
    body: JSON.stringify(body),
  })
}

export function createDiscoveryKeyword(keyword: string): Promise<DiscoveryKeywordItem> {
  const body: CreateDiscoveryKeywordBody = { keyword }

  return apiRequest<DiscoveryKeywordItem>('/api/discovery-keywords/', {
    method: 'POST',
    body: JSON.stringify(body),
  })
}

export function disableDiscoveryKeyword(keywordId: string): Promise<DiscoveryKeywordItem> {
  // The action takes no input, but every non-GET action still sends a JSON body.
  return apiRequest<DiscoveryKeywordItem>(`/api/discovery-keywords/${keywordId}/disable`, {
    method: 'POST',
    body: JSON.stringify({}),
  })
}

export function enableDiscoveryKeyword(keywordId: string): Promise<DiscoveryKeywordItem> {
  return apiRequest<DiscoveryKeywordItem>(`/api/discovery-keywords/${keywordId}/enable`, {
    method: 'POST',
    body: JSON.stringify({}),
  })
}

/** Soft deletes one keyword: the server keeps the row and keeps blocking its value. */
export function removeDiscoveryKeyword(keywordId: string): Promise<void> {
  return apiRequest<void>(`/api/discovery-keywords/${keywordId}`, {
    method: 'DELETE',
  })
}
