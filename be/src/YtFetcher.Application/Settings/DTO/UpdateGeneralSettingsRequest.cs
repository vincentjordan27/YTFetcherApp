namespace YtFetcher.Application.Settings.DTO;

public sealed record UpdateGeneralSettingsRequest(
    int QueriesDailyQuotaLimit,
    int VideoBatchGetStatsDailyQuotaLimit,
    int SearchQueriesDailyQuotaLimit,
    int DiscoveryMaxPagesPerKeywordPerRun,
    int MaxDailyApprovedChannels,
    int BackfillPageLimit,
    int BackfillDispatchBatchSize,
    long? MinimumSubscriberCount);
