/**
 * Wire contract for `GET /api/usage/today`. Field names match the HTTP response exactly; unit counts
 * are raw and are formatted in the browser. Each `*QuotaExhaustedAt` is an ISO timestamp or null.
 */
export interface TodayUsageApiResponse {
  /** ISO calendar date of the Pacific day the ledger row represents, for example `2026-10-01`. */
  readonly quotaDate: string
  readonly hasLedger: boolean
  readonly queriesUnitsUsed: number
  readonly queriesDailyQuotaLimit: number
  readonly videoBatchStatsUnitsUsed: number
  readonly videoBatchGetStatsDailyQuotaLimit: number
  readonly searchQueryUnitsUsed: number
  readonly searchQueriesDailyQuotaLimit: number
  readonly firstPageAttemptUsed: number
  readonly maxDailyApprovedChannels: number
  readonly searchQuotaExhaustedAt: string | null
  readonly queriesQuotaExhaustedAt: string | null
  readonly videoBatchStatsQuotaExhaustedAt: string | null
}
