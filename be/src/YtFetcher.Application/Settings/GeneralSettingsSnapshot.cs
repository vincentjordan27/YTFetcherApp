namespace YtFetcher.Application.Settings;

public sealed record GeneralSettingsSnapshot(
    int QueriesDailyQuotaLimit,
    int VideoBatchGetStatsDailyQuotaLimit,
    int SearchQueriesDailyQuotaLimit,
    int DiscoveryMaxPagesPerKeywordPerRun,
    int MaxDailyApprovedChannels,
    int BackfillPageLimit,
    int BackfillDispatchBatchSize,
    long? MinimumSubscriberCount)
{
    public static GeneralSettingsSnapshot Default { get; } = new(
        QueriesDailyQuotaLimit: 10000,
        VideoBatchGetStatsDailyQuotaLimit: 10000,
        SearchQueriesDailyQuotaLimit: 100,
        DiscoveryMaxPagesPerKeywordPerRun: 1,
        MaxDailyApprovedChannels: 100,
        BackfillPageLimit: 3,
        BackfillDispatchBatchSize: 10,
        MinimumSubscriberCount: null);
}
