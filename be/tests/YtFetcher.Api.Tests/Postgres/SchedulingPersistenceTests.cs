using Microsoft.EntityFrameworkCore;
using YtFetcher.Application.Channels.DTO.Result;
using YtFetcher.Core.Channels;
using YtFetcher.Core.Quotas;
using YtFetcher.Core.Users;
using YtFetcher.Infrastructure.Persistence;
using YtFetcher.Infrastructure.Persistence.Repositories;

namespace YtFetcher.Api.Tests.Postgres;

/// <summary>
/// PostgreSQL-only proof that two concurrent approvals for the same pending channel commit exactly one
/// transition and consume exactly one paired first-page slot. Skipped unless the runner supplies
/// <c>YTF_TEST_POSTGRES</c>.
/// </summary>
[Collection(PostgresProofCollection.Name)]
public class SchedulingPersistenceTests : IAsyncLifetime
{
    private static readonly DateTimeOffset Now = new(2026, 9, 30, 12, 0, 0, TimeSpan.Zero);

    private static readonly DateOnly QuotaDate = new(2026, 9, 30);

    private static readonly Guid ActorUserId = User.SystemUserId;

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
    public async Task Two_concurrent_approvals_commit_exactly_one_transition_and_one_slot()
    {
        await SeedWindowAsync();
        var channel = await SeedPendingChannelAsync();

        await using var firstContext = CreateContext();
        await using var secondContext = CreateContext();
        var firstRepository = new ChannelRepository(firstContext);
        var secondRepository = new ChannelRepository(secondContext);

        var results = await Task.WhenAll(
            firstRepository.TryApproveForSchedulingAsync(channel.Id, QuotaDate, ActorUserId, Now, CancellationToken.None),
            secondRepository.TryApproveForSchedulingAsync(channel.Id, QuotaDate, ActorUserId, Now, CancellationToken.None));

        Assert.Single(results, result => result.Status == ChannelApprovalStatus.Succeeded);

        var stored = await ReadChannelAsync(channel.Id);
        Assert.Equal(ChannelStatus.APPROVED, stored!.Status);
        Assert.Equal(BackfillStatus.SCHEDULING, stored.BackfillStatus);
        Assert.Equal(QuotaDate, stored.FirstPageQuotaDate);

        var ledger = await dbContext.YouTubeDailyQuotaUsages.AsNoTracking().SingleAsync(entity => entity.QuotaDate == QuotaDate);
        Assert.Equal(1, ledger.FirstPageAttemptUsed);
        Assert.Equal(1, ledger.QueriesUnitsUsed);
        Assert.Equal(1, ledger.VideoBatchStatsUnitsUsed);
    }

    [PostgresProofFact]
    public async Task Approval_is_refused_with_503_when_the_window_was_never_initialized()
    {
        var channel = await SeedPendingChannelAsync();

        var result = await CreateRepository().TryApproveForSchedulingAsync(
            channel.Id,
            QuotaDate,
            ActorUserId,
            Now,
            CancellationToken.None);

        Assert.Equal(ChannelApprovalStatus.WindowNotReady, result.Status);

        var stored = await ReadChannelAsync(channel.Id);
        Assert.Equal(ChannelStatus.PENDING, stored!.Status);
    }

    [PostgresProofFact]
    public async Task Approval_is_refused_when_a_required_first_page_bucket_is_known_exhausted()
    {
        await SeedWindowAsync();
        var channel = await SeedPendingChannelAsync();
        await MarkExhaustedAsync(queriesBucket: true);

        var result = await CreateRepository().TryApproveForSchedulingAsync(
            channel.Id,
            QuotaDate,
            ActorUserId,
            Now,
            CancellationToken.None);

        // The conditional update matches zero rows, so no slot and no bucket unit is consumed.
        Assert.Equal(ChannelApprovalStatus.LimitReached, result.Status);
        await AssertNoReservationAsync(channel.Id);
    }

    [PostgresProofFact]
    public async Task Approval_is_refused_when_the_batch_stats_bucket_is_known_exhausted()
    {
        await SeedWindowAsync();
        var channel = await SeedPendingChannelAsync();
        await MarkExhaustedAsync(queriesBucket: false);

        var result = await CreateRepository().TryApproveForSchedulingAsync(
            channel.Id,
            QuotaDate,
            ActorUserId,
            Now,
            CancellationToken.None);

        Assert.Equal(ChannelApprovalStatus.LimitReached, result.Status);
        await AssertNoReservationAsync(channel.Id);
    }

    private YtFetcherDbContext CreateContext() =>
        new(new DbContextOptionsBuilder<YtFetcherDbContext>().UseNpgsql(connectionString).Options);

    private ChannelRepository CreateRepository() => new(dbContext);

    private async Task SeedWindowAsync()
    {
        dbContext.YouTubeDailyQuotaUsages.Add(new YouTubeDailyQuotaUsage
        {
            Id = Guid.NewGuid(),
            QuotaDate = QuotaDate,
            FirstPageAttemptLimit = 500,
            FirstPageAttemptUsed = 0,
            IsDeleted = false,
            CreatedAt = Now,
            ModifiedAt = Now
        });

        await dbContext.SaveChangesAsync();
    }

    private async Task<Channel> SeedPendingChannelAsync()
    {
        var channel = new Channel
        {
            Id = Guid.NewGuid(),
            YoutubeChannelId = $"UC-{Guid.NewGuid():N}",
            Name = "Scheduling proof channel",
            Status = ChannelStatus.PENDING,
            BackfillStatus = BackfillStatus.PENDING,
            IsDeleted = false,
            DiscoveredAt = Now.AddDays(-2),
            CreatedAt = Now.AddDays(-2),
            ModifiedAt = Now.AddDays(-1)
        };

        dbContext.Channels.Add(channel);
        await dbContext.SaveChangesAsync();
        return channel;
    }

    private async Task<Channel?> ReadChannelAsync(Guid channelId) =>
        await dbContext.Channels.AsNoTracking().SingleOrDefaultAsync(channel => channel.Id == channelId);

    private Task MarkExhaustedAsync(bool queriesBucket)
    {
        if (queriesBucket)
        {
            return dbContext.Database.ExecuteSqlInterpolatedAsync(
                $"UPDATE youtube_daily_quota_usage SET queries_quota_exhausted_at = {Now} WHERE quota_date = {QuotaDate}");
        }

        return dbContext.Database.ExecuteSqlInterpolatedAsync(
            $"UPDATE youtube_daily_quota_usage SET video_batch_stats_quota_exhausted_at = {Now} WHERE quota_date = {QuotaDate}");
    }

    private async Task AssertNoReservationAsync(Guid channelId)
    {
        var ledger = await dbContext.YouTubeDailyQuotaUsages.AsNoTracking().SingleAsync(entity => entity.QuotaDate == QuotaDate);
        Assert.Equal(0, ledger.FirstPageAttemptUsed);
        Assert.Equal(0, ledger.QueriesUnitsUsed);
        Assert.Equal(0, ledger.VideoBatchStatsUnitsUsed);

        var stored = await ReadChannelAsync(channelId);
        Assert.Equal(ChannelStatus.PENDING, stored!.Status);
        Assert.Null(stored.FirstPageQuotaDate);
    }
}
