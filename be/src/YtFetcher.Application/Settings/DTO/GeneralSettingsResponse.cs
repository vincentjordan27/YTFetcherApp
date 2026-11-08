namespace YtFetcher.Application.Settings.DTO;

public sealed record GeneralSettingsResponse(
    int QueriesDailyQuotaLimit,
    int VideoBatchGetStatsDailyQuotaLimit,
    int SearchQueriesDailyQuotaLimit,
    int DiscoveryMaxPagesPerKeywordPerRun,
    int MaxDailyApprovedChannels,
    int BackfillPageLimit,
    int BackfillDispatchBatchSize,
    long? MinimumSubscriberCount);
