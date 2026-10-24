namespace YtFetcher.Application.Usage.DTO.Response;

/// <summary>
/// The current Pacific calendar day's provider-bucket ledger, as consumed by the Usage page. Values are raw
/// unit counts; number formatting belongs to the client. When <paramref name="HasLedger"/> is false the used
/// counters are zeros, while the limits still reflect configuration.
/// </summary>
public sealed record TodayUsageResponse(
    DateOnly QuotaDate,
    bool HasLedger,
    int QueriesUnitsUsed,
    int QueriesDailyQuotaLimit,
    int VideoBatchStatsUnitsUsed,
    int VideoBatchGetStatsDailyQuotaLimit,
    int SearchQueryUnitsUsed,
    int SearchQueriesDailyQuotaLimit,
    int FirstPageAttemptUsed,
    int MaxDailyApprovedChannels,
    DateTimeOffset? SearchQuotaExhaustedAt,
    DateTimeOffset? QueriesQuotaExhaustedAt,
    DateTimeOffset? VideoBatchStatsQuotaExhaustedAt);
