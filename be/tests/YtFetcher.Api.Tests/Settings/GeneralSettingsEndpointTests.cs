using System.Net;
using System.Net.Http.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using YtFetcher.Api.Tests.Channels;
using YtFetcher.Application.Settings;
using YtFetcher.Core.Settings;
using YtFetcher.Core.Users;
using YtFetcher.Infrastructure.Persistence;

namespace YtFetcher.Api.Tests.Settings;

[Collection(ApiStartupCollection.Name)]
public sealed class GeneralSettingsEndpointTests
{
    [Fact]
    public async Task Get_returns_the_persisted_settings_without_an_api_key()
    {
        using var configuration = new HostConfigurationScope();
        using var application = new ChannelApiFactory();
        using var client = application.CreateClient();
        await SeedAsync(application.Services, GeneralSettingsSnapshot.Default);

        var response = await client.GetAsync("/api/settings/general");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var settings = await response.Content.ReadFromJsonAsync<SettingsPayload>();
        Assert.NotNull(settings);
        Assert.Equal(10000, settings.QueriesDailyQuotaLimit);
        Assert.Null(settings.MinimumSubscriberCount);
    }

    [Fact]
    public async Task Put_updates_all_settings_and_rejects_an_invalid_update_without_changing_them()
    {
        using var configuration = new HostConfigurationScope();
        using var application = new ChannelApiFactory();
        using var client = application.CreateClient();
        await SeedAsync(application.Services, GeneralSettingsSnapshot.Default);

        var updated = await client.PutAsJsonAsync(
            "/api/settings/general",
            new
            {
                queriesDailyQuotaLimit = 20000,
                videoBatchGetStatsDailyQuotaLimit = 15000,
                searchQueriesDailyQuotaLimit = 200,
                discoveryMaxPagesPerKeywordPerRun = 2,
                maxDailyApprovedChannels = 250,
                backfillPageLimit = 5,
                backfillDispatchBatchSize = 20,
                minimumSubscriberCount = 1000
            });
        var invalid = await client.PutAsJsonAsync(
            "/api/settings/general",
            new
            {
                queriesDailyQuotaLimit = 10,
                videoBatchGetStatsDailyQuotaLimit = 10,
                searchQueriesDailyQuotaLimit = 100,
                discoveryMaxPagesPerKeywordPerRun = 1,
                maxDailyApprovedChannels = 11,
                backfillPageLimit = 3,
                backfillDispatchBatchSize = 10,
                minimumSubscriberCount = (long?)null
            });
        var current = await client.GetFromJsonAsync<SettingsPayload>("/api/settings/general");

        Assert.Equal(HttpStatusCode.OK, updated.StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, invalid.StatusCode);
        Assert.NotNull(current);
        Assert.Equal(20000, current.QueriesDailyQuotaLimit);
        Assert.Equal(1000, current.MinimumSubscriberCount);
    }

    private static async Task SeedAsync(IServiceProvider services, GeneralSettingsSnapshot settings)
    {
        using var scope = services.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<YtFetcherDbContext>();
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

    private sealed record SettingsPayload(
        int QueriesDailyQuotaLimit,
        int VideoBatchGetStatsDailyQuotaLimit,
        int SearchQueriesDailyQuotaLimit,
        int DiscoveryMaxPagesPerKeywordPerRun,
        int MaxDailyApprovedChannels,
        int BackfillPageLimit,
        int BackfillDispatchBatchSize,
        long? MinimumSubscriberCount);
}
