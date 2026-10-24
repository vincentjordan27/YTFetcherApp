import type { TodayUsageApiResponse } from '@/app/usage/api/types/today-usage-api'
import type { TodayUsage } from '@/app/usage/types/today-usage'

const unitFormatter = new Intl.NumberFormat('en-US')

const exhaustedFormatter = new Intl.DateTimeFormat('en-GB', {
  day: '2-digit',
  month: 'short',
  year: 'numeric',
  hour: '2-digit',
  minute: '2-digit',
  hour12: false,
})

/** Formats the per-bucket exhaustion marker in the viewer's timezone, or null while the bucket is available. */
function exhaustedLabel(value: string | null): string | null {
  if (value === null) {
    return null
  }

  const instant = new Date(value)
  return Number.isNaN(instant.getTime())
    ? 'Quota limit reached'
    : `Quota limit reached · ${exhaustedFormatter.format(instant)}`
}

/**
 * Maps one API response to the display model. Unit formatting happens here, never on the server, and the
 * first-page cap is rendered in channels with the configured value the API returned.
 */
export function toTodayUsage(response: TodayUsageApiResponse): TodayUsage {
  return {
    quotaDate: response.quotaDate,
    buckets: [
      {
        id: 'search',
        label: 'Search Queries per day',
        usedLabel: unitFormatter.format(response.searchQueryUnitsUsed),
        limitLabel: unitFormatter.format(response.searchQueriesDailyQuotaLimit),
        hint: 'Used by channel discovery searches.',
        exhaustedLabel: exhaustedLabel(response.searchQuotaExhaustedAt),
      },
      {
        id: 'queries',
        label: 'Queries per day',
        usedLabel: unitFormatter.format(response.queriesUnitsUsed),
        limitLabel: unitFormatter.format(response.queriesDailyQuotaLimit),
        hint: 'Used by channel lookups, upload checks, and backfill playlist pages.',
        exhaustedLabel: exhaustedLabel(response.queriesQuotaExhaustedAt),
      },
      {
        id: 'batch-stats',
        label: 'Video Batch Get Stats',
        usedLabel: unitFormatter.format(response.videoBatchStatsUnitsUsed),
        limitLabel: unitFormatter.format(response.videoBatchGetStatsDailyQuotaLimit),
        hint: 'Used by daily video updates, new-upload details, and backfill video details.',
        exhaustedLabel: exhaustedLabel(response.videoBatchStatsQuotaExhaustedAt),
      },
    ],
    firstPageCap: {
      usedLabel: unitFormatter.format(response.firstPageAttemptUsed),
      limitLabel: `${unitFormatter.format(response.maxDailyApprovedChannels)} channels/day`,
      hint: 'Each approved channel reserves one Query and one Video Batch Stats unit before its first backfill page runs.',
    },
  }
}
