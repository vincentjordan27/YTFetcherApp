using Microsoft.EntityFrameworkCore;
using YtFetcher.Application.Abstractions.Repositories;
using YtFetcher.Application.Settings;
using YtFetcher.Core.Settings;

namespace YtFetcher.Infrastructure.Persistence.Repositories;

public sealed class GeneralSettingsRepository(
    YtFetcherDbContext dbContext,
    TimeProvider timeProvider) : IGeneralSettingsRepository
{
    public async Task<GeneralSettingsSnapshot> GetRequiredAsync(CancellationToken cancellationToken)
    {
        var settings = await dbContext.GeneralSettings
            .AsNoTracking()
            .SingleAsync(candidate => candidate.Id == GeneralSettings.SingletonId, cancellationToken);

        return ToSnapshot(settings);
    }

    public async Task<GeneralSettingsSnapshot> UpdateAsync(
        GeneralSettingsSnapshot settings,
        Guid? actorUserId,
        CancellationToken cancellationToken)
    {
        var entity = await dbContext.GeneralSettings
            .SingleAsync(candidate => candidate.Id == GeneralSettings.SingletonId, cancellationToken);

        entity.QueriesDailyQuotaLimit = settings.QueriesDailyQuotaLimit;
        entity.VideoBatchGetStatsDailyQuotaLimit = settings.VideoBatchGetStatsDailyQuotaLimit;
        entity.SearchQueriesDailyQuotaLimit = settings.SearchQueriesDailyQuotaLimit;
        entity.DiscoveryMaxPagesPerKeywordPerRun = settings.DiscoveryMaxPagesPerKeywordPerRun;
        entity.MaxDailyApprovedChannels = settings.MaxDailyApprovedChannels;
        entity.BackfillPageLimit = settings.BackfillPageLimit;
        entity.BackfillDispatchBatchSize = settings.BackfillDispatchBatchSize;
        entity.MinimumSubscriberCount = settings.MinimumSubscriberCount;
        entity.ModifiedAt = timeProvider.GetUtcNow();
        entity.ModifiedByUserId = actorUserId;

        await dbContext.SaveChangesAsync(cancellationToken);
        return ToSnapshot(entity);
    }

    private static GeneralSettingsSnapshot ToSnapshot(GeneralSettings settings) => new(
        settings.QueriesDailyQuotaLimit,
        settings.VideoBatchGetStatsDailyQuotaLimit,
        settings.SearchQueriesDailyQuotaLimit,
        settings.DiscoveryMaxPagesPerKeywordPerRun,
        settings.MaxDailyApprovedChannels,
        settings.BackfillPageLimit,
        settings.BackfillDispatchBatchSize,
        settings.MinimumSubscriberCount);
}
