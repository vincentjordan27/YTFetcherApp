/** Presentation state of the Usage page while today's ledger is requested. */
export type TodayUsageState = 'ready' | 'loading' | 'error'

/** One provider quota bucket, already formatted for display. */
export interface TodayUsageBucketRow {
  readonly id: string
  readonly label: string
  /** Used units, formatted. */
  readonly usedLabel: string
  /** Configured daily limit, formatted. */
  readonly limitLabel: string
  /** Short static explanatory copy; presentation only. */
  readonly hint: string
  /** `Quota limit reached` with a localized timestamp, or null while the bucket is available. */
  readonly exhaustedLabel: string | null
}

/** The configured first-page reservation state, shown in channels, not API units. */
export interface TodayUsageFirstPageCap {
  readonly usedLabel: string
  readonly limitLabel: string
  readonly hint: string
}

/** The current Pacific day's ledger, already formatted for display. */
export interface TodayUsage {
  readonly quotaDate: string
  readonly buckets: readonly TodayUsageBucketRow[]
  readonly firstPageCap: TodayUsageFirstPageCap
}
