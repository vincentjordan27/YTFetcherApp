import { apiRequest } from '@/common/api/api-client'

export interface GeneralSettings {
  queriesDailyQuotaLimit: number
  videoBatchGetStatsDailyQuotaLimit: number
  searchQueriesDailyQuotaLimit: number
  discoveryMaxPagesPerKeywordPerRun: number
  maxDailyApprovedChannels: number
  backfillPageLimit: number
  backfillDispatchBatchSize: number
  minimumSubscriberCount: number | null
}

export const getGeneralSettings = () => apiRequest<GeneralSettings>('/api/settings/general')

export const updateGeneralSettings = (settings: GeneralSettings) =>
  apiRequest<GeneralSettings>('/api/settings/general', {
    method: 'PUT',
    body: JSON.stringify(settings),
  })
