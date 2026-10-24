using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using YtFetcher.Application.Backfill.DTO.Result;
using YtFetcher.Core.BackfillRuns;
using YtFetcher.Core.Channels;
using YtFetcher.Core.Quotas;
using YtFetcher.Core.Users;
using YtFetcher.Infrastructure.Persistence;
using YtFetcher.Infrastructure.Persistence.Repositories;

namespace YtFetcher.Api.Tests.Persistence;

/// <summary>
/// First-page rollover: an approval can commit just before Pacific midnight and its Hangfire page can start
/// after it, and a provider failure can leave page one unfinished into a later date. The claim transaction must
/// reclaim a fresh paired reservation for the current Pacific date instead of turning the stale date into a
/// technical failure. These run on the InMemory provider because the repository keeps an equivalent tracked
/// path for it; the PostgreSQL conditional-update path is covered by the PostgreSQL proof suite.
/// </summary>
public class BackfillRepositoryRolloverTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 6, 8, 0, 0, TimeSpan.Zero);

    private static readonly DateOnly QuotaDate = new(2026, 10, 6);

    [Fact]
    public async Task Claiming_a_page_attributes_the_channel_and_new_run_to_the_system_user()
    {
        await using var context = CreateContext();
        var channel = AddChannel(context, firstPageQuotaDate: QuotaDate);
        await context.SaveChangesAsync();

        await CreateRepository(context).TryClaimPageAsync(
            channel.Id,
            Guid.NewGuid(),
            QuotaDate,
            BackfillTriggerSource.APPROVAL,
            Now,
            CancellationToken.None);

        var storedChannel = await context.Channels.AsNoTracking().SingleAsync(entity => entity.Id == channel.Id);
        var run = await context.ChannelBackfillRuns.AsNoTracking().SingleAsync();

        Assert.Equal(User.SystemUserId, storedChannel.ModifiedByUserId);
        Assert.Equal(User.SystemUserId, run.CreatedByUserId);
        Assert.Equal(User.SystemUserId, run.ModifiedByUserId);
    }

    [Fact]
    public async Task Persisting_a_page_attributes_channel_video_snapshot_and_run_to_the_system_user()
    {
        await using var context = CreateContext();
        var channel = AddChannel(context, firstPageQuotaDate: QuotaDate);
        await context.SaveChangesAsync();
        var repository = CreateRepository(context);
        var claimToken = Guid.NewGuid();

        await repository.TryClaimPageAsync(
            channel.Id,
            claimToken,
            QuotaDate,
            BackfillTriggerSource.APPROVAL,
            Now,
            CancellationToken.None);

        await repository.PersistPageAsync(
            new BackfillPageResult(
                channel.Id,
                claimToken,
                "UU-rollover",
                [new UploadsPageItemResult("video-1", "Video 1", null, Now.AddDays(-1))],
                [new VideoStatsItemResult("video-1", 30_000, 20, 2)],
                NextPageToken: null,
                IsChannelComplete: true,
                RunStartedAt: Now),
            CancellationToken.None);

        var storedChannel = await context.Channels.AsNoTracking().SingleAsync(entity => entity.Id == channel.Id);
        var run = await context.ChannelBackfillRuns.AsNoTracking().SingleAsync();
        var video = await context.Videos.AsNoTracking().SingleAsync();
        var snapshot = await context.VideoSnapshots.AsNoTracking().SingleAsync();

        Assert.Equal(User.SystemUserId, storedChannel.ModifiedByUserId);
        Assert.Equal(User.SystemUserId, run.ModifiedByUserId);
        Assert.Equal(User.SystemUserId, video.CreatedByUserId);
        Assert.Equal(User.SystemUserId, video.ModifiedByUserId);
        Assert.Equal(User.SystemUserId, snapshot.CreatedByUserId);
        Assert.Equal(User.SystemUserId, snapshot.ModifiedByUserId);
    }

    [Fact]
    public async Task A_same_day_first_page_retry_reuses_the_saved_pair_with_no_extra_increment()
    {
        await using var context = CreateContext();
        var channel = AddChannel(context, firstPageQuotaDate: QuotaDate);
        AddLedger(context, firstPageAttemptLimit: 500, firstPageAttemptUsed: 1, queriesUsed: 1, batchStatsUsed: 1);
        await context.SaveChangesAsync();

        var claimed = await CreateRepository(context).TryClaimPageAsync(
            channel.Id,
            Guid.NewGuid(),
            QuotaDate,
            BackfillTriggerSource.APPROVAL,
            Now,
            CancellationToken.None);

        Assert.NotNull(claimed);

        var ledger = await context.YouTubeDailyQuotaUsages.AsNoTracking().SingleAsync();
        Assert.Equal(1, ledger.FirstPageAttemptUsed);
        Assert.Equal(1, ledger.QueriesUnitsUsed);
        Assert.Equal(1, ledger.VideoBatchStatsUnitsUsed);
    }

    [Fact]
    public async Task A_stale_date_gets_exactly_one_new_pair_and_the_current_date_update()
    {
        await using var context = CreateContext();
        var channel = AddChannel(context, firstPageQuotaDate: QuotaDate.AddDays(-1));
        AddLedger(context, firstPageAttemptLimit: 500, firstPageAttemptUsed: 0, queriesUsed: 0, batchStatsUsed: 0);
        await context.SaveChangesAsync();

        var claimed = await CreateRepository(context).TryClaimPageAsync(
            channel.Id,
            Guid.NewGuid(),
            QuotaDate,
            BackfillTriggerSource.APPROVAL,
            Now,
            CancellationToken.None);

        Assert.NotNull(claimed);

        var stored = await context.Channels.AsNoTracking().SingleAsync(entity => entity.Id == channel.Id);
        Assert.Equal(QuotaDate, stored.FirstPageQuotaDate);

        var ledger = await context.YouTubeDailyQuotaUsages.AsNoTracking().SingleAsync();
        Assert.Equal(1, ledger.FirstPageAttemptUsed);
        Assert.Equal(1, ledger.QueriesUnitsUsed);
        Assert.Equal(1, ledger.VideoBatchStatsUnitsUsed);

        // Exactly one run row was opened.
        Assert.Equal(1, await context.ChannelBackfillRuns.CountAsync());
    }

    [Fact]
    public async Task A_stale_date_at_the_daily_first_page_cap_returns_no_claim_and_no_run_row()
    {
        await using var context = CreateContext();
        var channel = AddChannel(context, firstPageQuotaDate: null);
        AddLedger(context, firstPageAttemptLimit: 1, firstPageAttemptUsed: 1, queriesUsed: 1, batchStatsUsed: 1);
        await context.SaveChangesAsync();

        var claimed = await CreateRepository(context).TryClaimPageAsync(
            channel.Id,
            Guid.NewGuid(),
            QuotaDate,
            BackfillTriggerSource.APPROVAL,
            Now,
            CancellationToken.None);

        Assert.Null(claimed);
        Assert.Equal(0, await context.ChannelBackfillRuns.CountAsync());

        var stored = await context.Channels.AsNoTracking().SingleAsync(entity => entity.Id == channel.Id);
        Assert.Null(stored.FirstPageQuotaDate);
        Assert.Equal(BackfillStatus.PENDING, stored.BackfillStatus);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task A_stale_date_under_a_known_bucket_exhaustion_returns_no_claim_and_no_run_row(bool queriesExhausted)
    {
        await using var context = CreateContext();
        var channel = AddChannel(context, firstPageQuotaDate: null);
        var ledger = AddLedger(context, firstPageAttemptLimit: 500, firstPageAttemptUsed: 0, queriesUsed: 0, batchStatsUsed: 0);

        if (queriesExhausted)
        {
            ledger.MarkQueriesQuotaExhausted(Now);
        }
        else
        {
            ledger.MarkVideoBatchStatsQuotaExhausted(Now);
        }

        await context.SaveChangesAsync();

        var claimed = await CreateRepository(context).TryClaimPageAsync(
            channel.Id,
            Guid.NewGuid(),
            QuotaDate,
            BackfillTriggerSource.APPROVAL,
            Now,
            CancellationToken.None);

        Assert.Null(claimed);
        Assert.Equal(0, await context.ChannelBackfillRuns.CountAsync());

        var after = await context.YouTubeDailyQuotaUsages.AsNoTracking().SingleAsync();
        Assert.Equal(0, after.FirstPageAttemptUsed);
        Assert.Equal(0, after.QueriesUnitsUsed);
        Assert.Equal(0, after.VideoBatchStatsUnitsUsed);
    }

    private static YtFetcherDbContext CreateContext() =>
        new(new DbContextOptionsBuilder<YtFetcherDbContext>()
            .UseInMemoryDatabase($"yt-fetcher-rollover-{Guid.NewGuid():N}")
            .ConfigureWarnings(warnings => warnings.Ignore(InMemoryEventId.TransactionIgnoredWarning))
            .Options);

    private static BackfillRepository CreateRepository(YtFetcherDbContext context) => new(context, TimeProvider.System);

    private static YouTubeDailyQuotaUsage AddLedger(
        YtFetcherDbContext context,
        int firstPageAttemptLimit,
        int firstPageAttemptUsed,
        int queriesUsed,
        int batchStatsUsed)
    {
        var ledger = new YouTubeDailyQuotaUsage
        {
            Id = Guid.NewGuid(),
            QuotaDate = QuotaDate,
            FirstPageAttemptLimit = firstPageAttemptLimit,
            FirstPageAttemptUsed = firstPageAttemptUsed,
            QueriesUnitsUsed = queriesUsed,
            VideoBatchStatsUnitsUsed = batchStatsUsed,
            IsDeleted = false,
            CreatedAt = Now,
            ModifiedAt = Now
        };

        context.YouTubeDailyQuotaUsages.Add(ledger);
        return ledger;
    }

    private static Channel AddChannel(YtFetcherDbContext context, DateOnly? firstPageQuotaDate)
    {
        var channel = new Channel
        {
            Id = Guid.NewGuid(),
            YoutubeChannelId = $"UC-{Guid.NewGuid():N}",
            Name = "Rollover channel",
            Status = ChannelStatus.APPROVED,
            BackfillStatus = BackfillStatus.PENDING,
            UploadsPlaylistId = "UU-rollover",
            BackfillReadyAt = Now.AddMinutes(-1),
            FirstPageQuotaDate = firstPageQuotaDate,
            IsDeleted = false,
            ApprovedAt = Now.AddDays(-1),
            DiscoveredAt = Now,
            CreatedAt = Now,
            ModifiedAt = Now
        };

        context.Channels.Add(channel);
        return channel;
    }
}
