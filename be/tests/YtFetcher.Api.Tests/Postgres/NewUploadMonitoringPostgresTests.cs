using Microsoft.EntityFrameworkCore;
using YtFetcher.Application.Abstractions.Services;
using YtFetcher.Application.Quotas;
using YtFetcher.Application.Quotas.DTO.Result;
using YtFetcher.Application.Videos.DTO.Result;
using YtFetcher.Core.Channels;
using YtFetcher.Core.Quotas;
using YtFetcher.Core.Videos;
using YtFetcher.Infrastructure.Persistence;
using YtFetcher.Infrastructure.Persistence.Repositories;

namespace YtFetcher.Api.Tests.Postgres;

/// <summary>
/// PostgreSQL-only proofs for M6 monitoring persistence and its bucket consumption. The candidate read, the
/// known-id lookup, and the <c>ON CONFLICT</c> insert path are decided by SQL, so none of it can be proven from
/// an EF InMemory run. Skipped unless the runner supplies <c>YTF_TEST_POSTGRES</c>.
/// </summary>
[Collection(PostgresProofCollection.Name)]
public class NewUploadMonitoringPostgresTests : IAsyncLifetime
{
    private static readonly DateTimeOffset Now = new(2026, 10, 6, 8, 0, 0, TimeSpan.Zero);

    private static readonly DateOnly QuotaDate = new(2026, 10, 6);

    private static readonly DateTimeOffset QuotaDateStart = new(2026, 10, 6, 7, 0, 0, TimeSpan.Zero);

    private readonly string connectionString = PostgresProofTarget.ConnectionString ?? string.Empty;

    private YtFetcherDbContext dbContext = null!;

    public async Task InitializeAsync()
    {
        if (!PostgresProofTarget.IsAvailable)
        {
            return;
        }

        dbContext = CreateContext();
        await PostgresProofDatabase.RecreateBaselineAsync(dbContext);
    }

    public async Task DisposeAsync()
    {
        if (!PostgresProofTarget.IsAvailable)
        {
            return;
        }

        await PostgresProofDatabase.ResetAsync(dbContext);
        await dbContext.DisposeAsync();
    }

    [PostgresProofFact]
    public async Task The_candidate_read_filters_and_orders_on_postgresql()
    {
        var nullMarker = await SeedCompletedChannelAsync(lastUploadCheckAt: null);
        var olderMarker = await SeedCompletedChannelAsync(lastUploadCheckAt: QuotaDateStart.AddHours(-2));

        // Excluded: already checked this Pacific date or wrong lifecycle.
        await SeedCompletedChannelAsync(lastUploadCheckAt: QuotaDateStart);
        await SeedChannelAsync(ChannelStatus.PENDING, BackfillStatus.COMPLETED);
        var running = await SeedChannelAsync(ChannelStatus.APPROVED, BackfillStatus.RUNNING);
        running.LastUploadCheckAt = QuotaDateStart.AddHours(-1);
        await dbContext.SaveChangesAsync();

        var candidates = await CreateRepository().GetUploadMonitorCandidatesAsync(
            QuotaDateStart,
            CancellationToken.None);

        Assert.Equal([nullMarker.Id, olderMarker.Id, running.Id], candidates.Select(channel => channel.Id));
    }

    [PostgresProofFact]
    public async Task Known_ids_include_soft_deleted_videos()
    {
        var channel = await SeedCompletedChannelAsync(lastUploadCheckAt: null);
        await SeedVideoAsync(channel.Id, "known-1", isDeleted: false);
        await SeedVideoAsync(channel.Id, "known-deleted", isDeleted: true);

        var known = await CreateRepository().GetKnownYoutubeVideoIdsAsync(
            ["known-1", "known-deleted", "missing"],
            CancellationToken.None);

        Assert.Contains("known-1", known);
        Assert.Contains("known-deleted", known);
        Assert.DoesNotContain("missing", known);
    }

    [PostgresProofFact]
    public async Task A_monitor_page_inserts_a_video_with_one_snapshot_and_advances_the_marker()
    {
        var (channel, token) = await SeedClaimedDateWithChannelAsync();

        var result = await CreateRepository().PersistUploadCheckAsync(
            new NewUploadCheckRequest(
                QuotaDate,
                channel.Id,
                token,
                Now.AddMinutes(-5),
                "UU-resolved",
                [new NewUploadCheckItem("new-1", "New", null, Now.AddHours(-1), 45_000, 200, 9)]),
            CancellationToken.None);

        Assert.True(result.OwnershipRetained);
        Assert.True(result.ChannelStillEligible);
        Assert.Equal(1, result.InsertedCount);

        var stored = await dbContext.Channels.AsNoTracking().SingleAsync(entity => entity.Id == channel.Id);
        Assert.Equal(Now.AddMinutes(-5), stored.LastUploadCheckAt);
        Assert.Equal("UU-resolved", stored.UploadsPlaylistId);

        var video = await dbContext.Videos.AsNoTracking().SingleAsync(entity => entity.YoutubeVideoId == "new-1");
        var snapshot = await dbContext.VideoSnapshots.AsNoTracking().SingleAsync(snapshot => snapshot.VideoId == video.Id);
        Assert.Equal(snapshot.CapturedAt, video.LastStatsFetchedAt);
    }

    [PostgresProofFact]
    public async Task An_approved_channel_with_an_incomplete_backfill_can_persist_its_daily_upload_check()
    {
        await CreateQuotaRepository().EnsureLedgerAsync(QuotaDate, CancellationToken.None);
        var token = Guid.NewGuid();
        await CreateQuotaRepository().TryClaimDailyProcessAsync(QuotaDate, token, Now, CancellationToken.None);
        var channel = await SeedChannelAsync(ChannelStatus.APPROVED, BackfillStatus.RUNNING);

        var result = await CreateRepository().PersistUploadCheckAsync(
            new NewUploadCheckRequest(
                QuotaDate,
                channel.Id,
                token,
                Now.AddMinutes(-5),
                "UU-proof",
                [new NewUploadCheckItem("new-running", "New", null, Now.AddHours(-1), 45_000, 200, 9)]),
            CancellationToken.None);

        Assert.True(result.OwnershipRetained);
        Assert.True(result.ChannelStillEligible);

        var stored = await dbContext.Channels.AsNoTracking().SingleAsync(entity => entity.Id == channel.Id);
        Assert.Equal(Now.AddMinutes(-5), stored.LastUploadCheckAt);
        Assert.Equal(1, await dbContext.Videos.CountAsync(video => video.YoutubeVideoId == "new-running"));
    }

    [PostgresProofFact]
    public async Task A_repeated_monitor_page_inserts_no_duplicate_video_or_snapshot()
    {
        var (channel, token) = await SeedClaimedDateWithChannelAsync();
        var request = new NewUploadCheckRequest(
            QuotaDate,
            channel.Id,
            token,
            Now.AddMinutes(-5),
            "UU-resolved",
            [new NewUploadCheckItem("new-1", "New", null, Now.AddHours(-1), 45_000, 200, 9)]);

        var first = await CreateRepository().PersistUploadCheckAsync(request, CancellationToken.None);
        var second = await CreateRepository().PersistUploadCheckAsync(request, CancellationToken.None);

        Assert.Equal(1, first.InsertedCount);
        Assert.Equal(0, second.InsertedCount);
        Assert.Equal(1, second.SkippedKnownCount);

        var video = await dbContext.Videos.AsNoTracking().SingleAsync(entity => entity.YoutubeVideoId == "new-1");
        Assert.Equal(1, await dbContext.VideoSnapshots.CountAsync(snapshot => snapshot.VideoId == video.Id));
    }

    [PostgresProofFact]
    public async Task A_stale_daily_token_cannot_persist_a_monitor_page()
    {
        var (channel, token) = await SeedClaimedDateWithChannelAsync();
        await CreateQuotaRepository().ReleaseDailyProcessClaimAsync(QuotaDate, token, CancellationToken.None);
        await CreateQuotaRepository().TryClaimDailyProcessAsync(QuotaDate, Guid.NewGuid(), Now, CancellationToken.None);

        var result = await CreateRepository().PersistUploadCheckAsync(
            new NewUploadCheckRequest(
                QuotaDate,
                channel.Id,
                token,
                Now.AddMinutes(-5),
                null,
                [new NewUploadCheckItem("new-1", "New", null, Now.AddHours(-1), 45_000, 200, 9)]),
            CancellationToken.None);

        Assert.False(result.OwnershipRetained);
        Assert.Equal(0, await dbContext.Videos.CountAsync(video => video.YoutubeVideoId == "new-1"));
        Assert.Equal(0, await dbContext.VideoSnapshots.CountAsync());

        // The stale owner must not advance the channel's success marker either.
        var stored = await dbContext.Channels.AsNoTracking().SingleAsync(entity => entity.Id == channel.Id);
        Assert.Null(stored.LastUploadCheckAt);
    }

    [PostgresProofFact]
    public async Task A_soft_deleted_channel_does_not_advance_its_marker()
    {
        var (channel, token) = await SeedClaimedDateWithChannelAsync();
        channel.IsDeleted = true;
        await dbContext.SaveChangesAsync();

        var result = await CreateRepository().PersistUploadCheckAsync(
            new NewUploadCheckRequest(
                QuotaDate,
                channel.Id,
                token,
                Now.AddMinutes(-5),
                null,
                [new NewUploadCheckItem("new-1", "New", null, Now.AddHours(-1), 45_000, 200, 9)]),
            CancellationToken.None);

        Assert.True(result.OwnershipRetained);
        Assert.False(result.ChannelStillEligible);
        Assert.Equal(0, await dbContext.Videos.CountAsync(video => video.YoutubeVideoId == "new-1"));
    }

    [PostgresProofFact]
    public async Task M6_consumes_a_queries_unit_and_only_a_batch_stats_unit_when_unknown_uploads_exist()
    {
        await SeedClaimedDateWithChannelAsync();
        Assert.Equal(
            QuotaReservationResult.Reserved,
            await CreateQuotaRepository().TryReserveQueriesUnitAsync(QuotaDate, null, CancellationToken.None));

        var ledger = await dbContext.YouTubeDailyQuotaUsages.AsNoTracking().SingleAsync(row => row.QuotaDate == QuotaDate);
        Assert.Equal(1, ledger.QueriesUnitsUsed);
        // No Batch Stats unit is spent until an existing unknown upload is actually looked up.
        Assert.Equal(0, ledger.VideoBatchStatsUnitsUsed);
    }

    private YtFetcherDbContext CreateContext() =>
        new(new DbContextOptionsBuilder<YtFetcherDbContext>().UseNpgsql(connectionString).Options);

    private NewUploadMonitoringRepository CreateRepository() => new(dbContext, TimeProvider.System);

    private DailyQuotaRepository CreateQuotaRepository() =>
        new(dbContext, new StubAppSettingsService(), TimeProvider.System);

    private async Task<(Channel Channel, Guid Token)> SeedClaimedDateWithChannelAsync()
    {
        await CreateQuotaRepository().EnsureLedgerAsync(QuotaDate, CancellationToken.None);
        var token = Guid.NewGuid();
        await CreateQuotaRepository().TryClaimDailyProcessAsync(QuotaDate, token, Now, CancellationToken.None);
        var channel = await SeedCompletedChannelAsync(lastUploadCheckAt: null);
        return (channel, token);
    }

    private async Task<Channel> SeedCompletedChannelAsync(DateTimeOffset? lastUploadCheckAt)
    {
        var channel = await SeedChannelAsync(ChannelStatus.APPROVED, BackfillStatus.COMPLETED);
        channel.LastUploadCheckAt = lastUploadCheckAt;
        await dbContext.SaveChangesAsync();
        return channel;
    }

    private async Task<Channel> SeedChannelAsync(ChannelStatus status, BackfillStatus backfillStatus)
    {
        var channel = new Channel
        {
            Id = Guid.NewGuid(),
            YoutubeChannelId = $"UC-{Guid.NewGuid():N}",
            Name = "M6 proof channel",
            Status = status,
            BackfillStatus = backfillStatus,
            UploadsPlaylistId = "UU-proof",
            IsDeleted = false,
            ApprovedAt = Now.AddDays(-1),
            DiscoveredAt = Now,
            CreatedAt = Now,
            ModifiedAt = Now
        };

        dbContext.Channels.Add(channel);
        await dbContext.SaveChangesAsync();
        return channel;
    }

    private async Task SeedVideoAsync(Guid channelId, string youtubeVideoId, bool isDeleted)
    {
        dbContext.Videos.Add(new Video
        {
            Id = Guid.NewGuid(),
            YoutubeVideoId = youtubeVideoId,
            ChannelId = channelId,
            Title = youtubeVideoId,
            PublishedAt = Now.AddDays(-2),
            DurationMillis = 30_000,
            CurrentViews = 10,
            CurrentLikes = 1,
            IsDeleted = isDeleted,
            CreatedAt = Now,
            ModifiedAt = Now
        });

        await dbContext.SaveChangesAsync();
    }

    private sealed class StubAppSettingsService : IAppSettingsService
    {
        public string? GetValue(string key) => key switch
        {
            QuotaConfigurationKeys.QueriesDailyQuotaLimit => "10000",
            QuotaConfigurationKeys.VideoBatchGetStatsDailyQuotaLimit => "10000",
            QuotaConfigurationKeys.SearchQueriesDailyQuotaLimit => "100",
            QuotaConfigurationKeys.MaxDailyApprovedChannels => "500",
            _ => null
        };

        public string GetRequiredValue(string key) =>
            GetValue(key) ?? throw new InvalidOperationException($"Missing {key}.");

        public T GetRequiredSection<T>(string sectionName) where T : class => throw new NotSupportedException();
    }
}
