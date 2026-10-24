import { apiRequest } from '@/common/api/api-client'
import type { TodayUsageApiResponse } from '@/app/usage/api/types/today-usage-api'

/** Read-only: no body, path parameter, or query parameter. */
export function getTodayUsage(): Promise<TodayUsageApiResponse> {
  return apiRequest<TodayUsageApiResponse>('/api/usage/today')
}
