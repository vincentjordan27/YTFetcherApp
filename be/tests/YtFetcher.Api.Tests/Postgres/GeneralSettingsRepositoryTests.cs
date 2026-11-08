using Microsoft.EntityFrameworkCore;
using YtFetcher.Application.Settings;
using YtFetcher.Core.Settings;
using YtFetcher.Core.Users;
using YtFetcher.Infrastructure.Persistence;
using YtFetcher.Infrastructure.Persistence.Repositories;
using YtFetcher.Infrastructure.Services;

namespace YtFetcher.Api.Tests.Postgres;

public sealed class GeneralSettingsRepositoryTests
{
    [Fact]
    public async Task GetRequiredAsync_reads_the_persisted_default_snapshot()
    {
        await using var dbContext = CreateContext();
        await SeedAsync(dbContext, GeneralSettingsSnapshot.Default);
        var provider = CreateProvider(dbContext);

        var first = await provider.GetRequiredAsync(CancellationToken.None);
        var second = await provider.GetRequiredAsync(CancellationToken.None);

        Assert.Equal(10000, first.QueriesDailyQuotaLimit);
        Assert.Equal(10000, first.VideoBatchGetStatsDailyQuotaLimit);
        Assert.Equal(100, first.SearchQueriesDailyQuotaLimit);
        Assert.Equal(1, first.DiscoveryMaxPagesPerKeywordPerRun);
        Assert.Equal(100, first.MaxDailyApprovedChannels);
        Assert.Equal(3, first.BackfillPageLimit);
        Assert.Equal(10, first.BackfillDispatchBatchSize);
        Assert.Null(first.MinimumSubscriberCount);
        Assert.Equal(first, second);
    }

    [Fact]
    public async Task UpdateAsync_persists_the_replacement_snapshot()
    {
        await using var dbContext = CreateContext();
        await SeedAsync(dbContext, GeneralSettingsSnapshot.Default);
        var repository = new GeneralSettingsRepository(dbContext, TimeProvider.System);
        var settings = await repository.UpdateAsync(
            new GeneralSettingsSnapshot(20000, 15000, 200, 2, 250, 5, 20, 1000),
            Guid.NewGuid(),
            CancellationToken.None);

        Assert.Equal(20000, settings.QueriesDailyQuotaLimit);
        Assert.Equal(15000, settings.VideoBatchGetStatsDailyQuotaLimit);
        Assert.Equal(200, settings.SearchQueriesDailyQuotaLimit);
        Assert.Equal(2, settings.DiscoveryMaxPagesPerKeywordPerRun);
        Assert.Equal(250, settings.MaxDailyApprovedChannels);
        Assert.Equal(5, settings.BackfillPageLimit);
        Assert.Equal(20, settings.BackfillDispatchBatchSize);
        Assert.Equal(1000, settings.MinimumSubscriberCount);
    }

    private static YtFetcherDbContext CreateContext(string? databaseName = null) =>
        new(new DbContextOptionsBuilder<YtFetcherDbContext>()
            .UseInMemoryDatabase(databaseName ?? Guid.NewGuid().ToString())
            .Options);

    private static GeneralSettingsProvider CreateProvider(YtFetcherDbContext dbContext) =>
        new(new GeneralSettingsRepository(dbContext, TimeProvider.System));

    private static async Task SeedAsync(YtFetcherDbContext dbContext, GeneralSettingsSnapshot settings)
    {
        dbContext.GeneralSettings.Add(new GeneralSettings
        {
            QueriesDailyQuotaLimit = settings.QueriesDailyQuotaLimit,
            VideoBatchGetStatsDailyQuotaLimit = settings.VideoBatchGetStatsDailyQuotaLimit,
            SearchQueriesDailyQuotaLimit = settings.SearchQueriesDailyQuotaLimit,
            DiscoveryMaxPagesPerKeywordPerRun = settings.DiscoveryMaxPagesPerKeywordPerRun,
            MaxDailyApprovedChannels = settings.MaxDailyApprovedChannels,
            BackfillPageLimit = settings.BackfillPageLimit,
            BackfillDispatchBatchSize = settings.BackfillDispatchBatchSize,
            MinimumSubscriberCount = settings.MinimumSubscriberCount,
            CreatedAt = DateTimeOffset.UtcNow,
            CreatedByUserId = User.SystemUserId,
            ModifiedAt = DateTimeOffset.UtcNow,
            ModifiedByUserId = User.SystemUserId
        });

        await dbContext.SaveChangesAsync();
    }
}
