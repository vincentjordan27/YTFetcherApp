using Microsoft.EntityFrameworkCore;
using YtFetcher.Application.Backfill.DTO.Result;
using YtFetcher.Core.BackfillRuns;
using YtFetcher.Core.Channels;
using YtFetcher.Core.Videos;
using YtFetcher.Infrastructure.Persistence;
using YtFetcher.Infrastructure.Persistence.Repositories;

namespace YtFetcher.Api.Tests.Postgres;

/// <summary>
/// PostgreSQL-only proofs for the backfill cross-table transaction, the quota-limit deferral, and stale-claim
/// recovery. Parent/child insert order, rollback, and row locks cannot be proven from an EF InMemory run.
/// Skipped unless the runner supplies <c>YTF_TEST_POSTGRES</c>.
/// </summary>
[Collection(PostgresProofCollection.Name)]
public class BackfillPersistenceTests : IAsyncLifetime
{
    private static readonly DateTimeOffset Now = new(2026, 10, 6, 8, 0, 0, TimeSpan.Zero);

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
    public async Task The_page_transaction_persists_videos_snapshots_progress_and_the_claim_release()
    {
        var (channel, claimToken) = await SeedClaimedChannelAsync();

        var result = await CreateRepository().PersistPageAsync(
            new BackfillPageResult(
                channel.Id,
                claimToken,
                "UU-proof",
                [new UploadsPageItemResult("v1", "Video 1", null, Now.AddDays(-2))],
                [new VideoStatsItemResult("v1", 30_000, 1_000, 50)],
                NextPageToken: "page-2",
                IsChannelComplete: false,
                RunStartedAt: Now),
            CancellationToken.None);

        Assert.Equal(1, result.VideosInserted);

        var video = await dbContext.Videos.AsNoTracking().SingleAsync(entity => entity.YoutubeVideoId == "v1");
        Assert.Equal(1, await dbContext.VideoSnapshots.CountAsync(snapshot => snapshot.VideoId == video.Id));

        var stored = await dbContext.Channels.AsNoTracking().SingleAsync(entity => entity.Id == channel.Id);
        Assert.Equal(1, stored.BackfillPagesCompleted);
        Assert.Equal(BackfillStatus.PENDING, stored.BackfillStatus);
        Assert.Equal("page-2", stored.BackfillPageToken);
        Assert.Null(stored.BackfillClaimToken);
    }

    [PostgresProofFact]
    public async Task A_soft_deleted_video_is_not_revived_and_receives_no_initial_snapshot()
    {
        var (channel, claimToken) = await SeedClaimedChannelAsync();
        dbContext.Videos.Add(new Video
        {
            Id = Guid.NewGuid(),
            YoutubeVideoId = "v1",
            ChannelId = channel.Id,
            Title = "Deleted",
            PublishedAt = Now.AddDays(-2),
            DurationMillis = 30_000,
            IsDeleted = true,
            CreatedAt = Now,
            ModifiedAt = Now
        });
        await dbContext.SaveChangesAsync();

        var result = await CreateRepository().PersistPageAsync(
            new BackfillPageResult(
                channel.Id,
                claimToken,
                "UU-proof",
                [new UploadsPageItemResult("v1", "Video 1", null, Now.AddDays(-2))],
                [new VideoStatsItemResult("v1", 30_000, 1_000, 50)],
                NextPageToken: null,
                IsChannelComplete: true,
                RunStartedAt: Now),
            CancellationToken.None);

        Assert.Equal(0, result.VideosInserted);
        Assert.Equal(1, await dbContext.Videos.CountAsync(entity => entity.YoutubeVideoId == "v1"));
    }

    [PostgresProofFact]
    public async Task A_quota_limit_deferral_closes_the_run_and_returns_the_channel_to_pending()
    {
        var (channel, claimToken) = await SeedClaimedChannelAsync();

        var deferred = await CreateRepository().DeferForQuotaLimitAsync(
            channel.Id,
            claimToken,
            QuotaLimitBucket.QUERIES,
            Now,
            CancellationToken.None);

        Assert.True(deferred);

        var stored = await dbContext.Channels.AsNoTracking().SingleAsync(entity => entity.Id == channel.Id);
        Assert.Equal(BackfillStatus.PENDING, stored.BackfillStatus);
        Assert.Equal(0, stored.BackfillFailureCount);
        Assert.Null(stored.BackfillClaimToken);

        var run = await dbContext.ChannelBackfillRuns.AsNoTracking().SingleAsync(row => row.ClaimToken == claimToken);
        Assert.Equal(BackfillRunStatus.QUOTA_LIMIT_REACHED, run.Status);
        Assert.Equal(QuotaLimitBucket.QUERIES, run.QuotaLimitBucket);
        Assert.Null(run.FailureReason);
        Assert.NotNull(run.CompletedAt);
    }

    [PostgresProofFact]
    public async Task A_failure_records_only_the_safe_summary_and_releases_the_claim()
    {
        var (channel, claimToken) = await SeedClaimedChannelAsync();

        var failure = await CreateRepository().RecordFailureAsync(
            channel.Id,
            claimToken,
            "Unable to retrieve uploads from YouTube. Try again later.",
            Now,
            CancellationToken.None);

        Assert.NotNull(failure);
        Assert.Equal(1, failure!.FailureCount);

        var stored = await dbContext.Channels.AsNoTracking().SingleAsync(entity => entity.Id == channel.Id);
        Assert.Null(stored.BackfillClaimToken);
        Assert.Equal(BackfillStatus.PENDING, stored.BackfillStatus);

        var run = await dbContext.ChannelBackfillRuns.AsNoTracking().SingleAsync(row => row.ClaimToken == claimToken);
        Assert.Equal(BackfillRunStatus.FAILED, run.Status);
    }

    [PostgresProofFact]
    public async Task Recovery_abandons_a_stale_running_claim_and_returns_the_channel_to_the_queue()
    {
        var (channel, claimToken) = await SeedClaimedChannelAsync();
        var recoveredAt = Now + Channel.BackfillClaimLifetime + TimeSpan.FromMinutes(1);

        var recovery = await CreateRepository().RecoverStaleWorkAsync(
            recoveredAt - Channel.BackfillClaimLifetime,
            recoveredAt,
            maximumCount: 10,
            CancellationToken.None);

        Assert.Equal(1, recovery.AbandonedRunCount);

        var stored = await dbContext.Channels.AsNoTracking().SingleAsync(entity => entity.Id == channel.Id);
        Assert.Equal(BackfillStatus.PENDING, stored.BackfillStatus);
        Assert.Null(stored.BackfillClaimToken);

        var run = await dbContext.ChannelBackfillRuns.AsNoTracking().SingleAsync(row => row.ClaimToken == claimToken);
        Assert.Equal(BackfillRunStatus.ABANDONED, run.Status);
    }

    [PostgresProofFact]
    public async Task Dispatch_selects_a_ready_pending_channel()
    {
        var channel = await SeedReadyPendingChannelAsync();

        var dispatch = await CreateRepository().DispatchCandidatesAsync(
            pageLimit: 0,
            utcNow: Now,
            maximumCount: 10,
            CancellationToken.None);

        Assert.Equal([channel.Id], dispatch.DispatchedChannelIds);
        Assert.Equal(BackfillStatus.SCHEDULING, (await dbContext.Channels.AsNoTracking().SingleAsync(entity => entity.Id == channel.Id)).BackfillStatus);
    }

    private YtFetcherDbContext CreateContext() =>
        new(new DbContextOptionsBuilder<YtFetcherDbContext>().UseNpgsql(connectionString).Options);

    private BackfillRepository CreateRepository() => new(dbContext, TimeProvider.System);

    private async Task<(Channel Channel, Guid ClaimToken)> SeedClaimedChannelAsync()
    {
        var channel = await SeedReadyPendingChannelAsync();
        var claimToken = Guid.NewGuid();

        var claimed = await CreateRepository().TryClaimPageAsync(
            channel.Id,
            claimToken,
            new DateOnly(2026, 10, 6),
            BackfillTriggerSource.APPROVAL,
            Now,
            CancellationToken.None);

        Assert.NotNull(claimed);
        return (claimed!, claimToken);
    }

    private async Task<Channel> SeedReadyPendingChannelAsync()
    {
        var channel = new Channel
        {
            Id = Guid.NewGuid(),
            YoutubeChannelId = $"UC-{Guid.NewGuid():N}",
            Name = "Backfill proof channel",
            Status = ChannelStatus.APPROVED,
            BackfillStatus = BackfillStatus.PENDING,
            UploadsPlaylistId = "UU-proof",
            // This fixture represents a channel already approved for today's first page. The approval flow
            // reserves that pair before Hangfire starts, so this proof must not accidentally exercise rollover.
            FirstPageQuotaDate = new DateOnly(2026, 10, 6),
            BackfillReadyAt = Now.AddMinutes(-1),
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
}
