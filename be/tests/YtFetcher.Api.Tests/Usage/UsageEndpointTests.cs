using System.Net;
using System.Text.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using YtFetcher.Api.Tests.Channels;
using YtFetcher.Core.Quotas;
using YtFetcher.Infrastructure.Persistence;

namespace YtFetcher.Api.Tests.Usage;

/// <summary>
/// The read-only today endpoint: its camelCase JSON contract for both the ledger-present and the
/// ledger-absent case. Absence is a normal <c>200</c> state, never a <c>404</c>.
/// </summary>
[Collection(ApiStartupCollection.Name)]
public class UsageEndpointTests
{
    /// <summary>17:30 UTC is 10:30 on 2026-10-01 in Pacific daylight time.</summary>
    private static readonly DateTimeOffset Now = new(2026, 10, 1, 17, 30, 0, TimeSpan.Zero);

    private static readonly DateOnly PacificToday = new(2026, 10, 1);

    private static readonly string[] ExpectedPropertyNames =
    [
        "quotaDate",
        "hasLedger",
        "queriesUnitsUsed",
        "queriesDailyQuotaLimit",
        "videoBatchStatsUnitsUsed",
        "videoBatchGetStatsDailyQuotaLimit",
        "searchQueryUnitsUsed",
        "searchQueriesDailyQuotaLimit",
        "firstPageAttemptUsed",
        "maxDailyApprovedChannels",
        "searchQuotaExhaustedAt",
        "queriesQuotaExhaustedAt",
        "videoBatchStatsQuotaExhaustedAt"
    ];

    [Fact]
    public async Task Today_returns_the_documented_json_shape_for_the_current_pacific_ledger_row()
    {
        using var factory = new ChannelApiFactory();
        using var hostConfiguration = new HostConfigurationScope();
        using var application = CreateHost(factory);
        await SeedLedgerAsync(application);
        using var client = application.CreateClient();

        var response = await client.GetAsync("/api/usage/today");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var root = document.RootElement;

        Assert.Equal(
            ExpectedPropertyNames.Order().ToArray(),
            root.EnumerateObject().Select(property => property.Name).Order().ToArray());

        Assert.Equal("2026-10-01", root.GetProperty("quotaDate").GetString());
        Assert.True(root.GetProperty("hasLedger").GetBoolean());
        Assert.Equal(300, root.GetProperty("queriesUnitsUsed").GetInt32());
        Assert.Equal(10000, root.GetProperty("queriesDailyQuotaLimit").GetInt32());
        Assert.Equal(400, root.GetProperty("videoBatchStatsUnitsUsed").GetInt32());
        Assert.Equal(10000, root.GetProperty("videoBatchGetStatsDailyQuotaLimit").GetInt32());
        Assert.Equal(12, root.GetProperty("searchQueryUnitsUsed").GetInt32());
        Assert.Equal(100, root.GetProperty("searchQueriesDailyQuotaLimit").GetInt32());
        Assert.Equal(40, root.GetProperty("firstPageAttemptUsed").GetInt32());
        Assert.Equal(500, root.GetProperty("maxDailyApprovedChannels").GetInt32());
        Assert.Equal(JsonValueKind.Null, root.GetProperty("searchQuotaExhaustedAt").ValueKind);
        Assert.Equal(JsonValueKind.Null, root.GetProperty("queriesQuotaExhaustedAt").ValueKind);
        Assert.Equal(JsonValueKind.Null, root.GetProperty("videoBatchStatsQuotaExhaustedAt").ValueKind);
    }

    [Fact]
    public async Task Today_returns_200_with_hasLedger_false_when_the_pacific_day_has_no_row()
    {
        using var factory = new ChannelApiFactory();
        using var hostConfiguration = new HostConfigurationScope();
        using var application = CreateHost(factory);
        using var client = application.CreateClient();

        var response = await client.GetAsync("/api/usage/today");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var root = document.RootElement;

        Assert.Equal(
            ExpectedPropertyNames.Order().ToArray(),
            root.EnumerateObject().Select(property => property.Name).Order().ToArray());

        Assert.Equal("2026-10-01", root.GetProperty("quotaDate").GetString());
        Assert.False(root.GetProperty("hasLedger").GetBoolean());
        Assert.Equal(0, root.GetProperty("queriesUnitsUsed").GetInt32());
        Assert.Equal(0, root.GetProperty("videoBatchStatsUnitsUsed").GetInt32());
        Assert.Equal(0, root.GetProperty("searchQueryUnitsUsed").GetInt32());
        Assert.Equal(0, root.GetProperty("firstPageAttemptUsed").GetInt32());

        // Limits are configuration, so they are still reported.
        Assert.Equal(10000, root.GetProperty("queriesDailyQuotaLimit").GetInt32());
        Assert.Equal(10000, root.GetProperty("videoBatchGetStatsDailyQuotaLimit").GetInt32());
        Assert.Equal(100, root.GetProperty("searchQueriesDailyQuotaLimit").GetInt32());
        Assert.Equal(500, root.GetProperty("maxDailyApprovedChannels").GetInt32());
    }

    [Fact]
    public async Task Today_does_not_allow_a_live_quota_ledger_response_to_be_cached()
    {
        using var factory = new ChannelApiFactory();
        using var hostConfiguration = new HostConfigurationScope();
        using var application = CreateHost(factory);
        using var client = application.CreateClient();

        var response = await client.GetAsync("/api/usage/today");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains("no-store", response.Headers.CacheControl?.ToString(), StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// The host uses the real clock, so the Pacific quota date is pinned to a fixed instant instead of
    /// depending on when the test runs.
    /// </summary>
    private static WebApplicationFactory<Program> CreateHost(ChannelApiFactory factory) =>
        factory.WithWebHostBuilder(builder => builder.ConfigureServices(services =>
        {
            services.RemoveAll<TimeProvider>();
            services.AddSingleton<TimeProvider>(new FixedTimeProvider(Now));
        }));

    private static async Task SeedLedgerAsync(WebApplicationFactory<Program> application)
    {
        using var scope = application.Services.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<YtFetcherDbContext>();

        context.YouTubeDailyQuotaUsages.Add(new YouTubeDailyQuotaUsage
        {
            Id = Guid.NewGuid(),
            QuotaDate = PacificToday,
            QueriesUnitsUsed = 300,
            VideoBatchStatsUnitsUsed = 400,
            SearchQueryUnitsUsed = 12,
            FirstPageAttemptLimit = 500,
            FirstPageAttemptUsed = 40,
            IsDeleted = false,
            CreatedAt = Now,
            ModifiedAt = Now
        });

        await context.SaveChangesAsync();
    }

    private sealed class FixedTimeProvider(DateTimeOffset utcNow) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => utcNow;
    }
}
