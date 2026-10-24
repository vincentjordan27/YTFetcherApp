using Microsoft.EntityFrameworkCore;
using YtFetcher.Application.Channels.DTO.Result;
using YtFetcher.Core.Channels;
using YtFetcher.Core.Quotas;
using YtFetcher.Core.Users;
using YtFetcher.Infrastructure.Persistence;
using YtFetcher.Infrastructure.Persistence.Repositories;

namespace YtFetcher.Api.Tests.Postgres;

/// <summary>
/// PostgreSQL-only proof that two simultaneous manual adds are race-safe and that the paired first-page slot
/// claim is atomic with the insert: the unique YouTube channel ID constraint lets exactly one insert commit,
/// the loser reads the durable row, and a refused slot writes neither a channel nor a bucket unit.
/// Skipped unless the runner supplies <c>YTF_TEST_POSTGRES</c>.
/// </summary>
[Collection(PostgresProofCollection.Name)]
public class ManualChannelCreatePostgresTests : IAsyncLifetime
{
    private static readonly DateTimeOffset Now = new(2026, 10, 1, 18, 0, 0, TimeSpan.Zero);

    private static readonly DateOnly QuotaDate = new(2026, 10, 1);

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
        await SeedWindowAsync(firstPageAttemptLimit: 500);
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
    public async Task Two_concurrent_manual_adds_create_one_channel_and_conflict_the_other()
    {
        var youtubeChannelId = $"UC-{Guid.NewGuid():N}";
        var first = Channel.CreateApprovedForScheduling(youtubeChannelId, "First", null, "UU-first", Now, User.SystemUserId);
        var second = Channel.CreateApprovedForScheduling(youtubeChannelId, "Second", null, "UU-second", Now, User.SystemUserId);

        await using var firstContext = CreateContext();
        await using var secondContext = CreateContext();
        var firstRepository = new ChannelRepository(firstContext);
        var secondRepository = new ChannelRepository(secondContext);

        var results = await Task.WhenAll(
            firstRepository.CreateApprovedForSchedulingAsync(first, QuotaDate, CancellationToken.None),
            secondRepository.CreateApprovedForSchedulingAsync(second, QuotaDate, CancellationToken.None));

        Assert.Single(results, result => result.Status == ChannelApprovalStatus.Succeeded);
        Assert.Single(results, result => result.Status == ChannelApprovalStatus.Duplicate);

        var winner = results.Single(result => result.Status == ChannelApprovalStatus.Succeeded).Channel;
        var loser = results.Single(result => result.Status == ChannelApprovalStatus.Duplicate).Channel;
        Assert.Equal(winner!.Id, loser!.Id);

        var rows = await dbContext.Channels
            .AsNoTracking()
            .Where(channel => channel.YoutubeChannelId == youtubeChannelId)
            .ToListAsync();

        var row = Assert.Single(rows);
        Assert.Equal(ChannelStatus.APPROVED, row.Status);
        Assert.Equal(BackfillStatus.SCHEDULING, row.BackfillStatus);
        Assert.Equal(QuotaDate, row.FirstPageQuotaDate);

        // Exactly one paired slot was consumed.
        var ledger = await dbContext.YouTubeDailyQuotaUsages.AsNoTracking().SingleAsync(entity => entity.QuotaDate == QuotaDate);
        Assert.Equal(1, ledger.FirstPageAttemptUsed);
        Assert.Equal(1, ledger.QueriesUnitsUsed);
        Assert.Equal(1, ledger.VideoBatchStatsUnitsUsed);
    }

    [PostgresProofFact]
    public async Task A_manual_add_after_the_daily_maximum_is_refused_and_writes_nothing()
    {
        await dbContext.YouTubeDailyQuotaUsages
            .Where(usage => usage.QuotaDate == QuotaDate)
            .ExecuteUpdateAsync(setters => setters
                .SetProperty(usage => usage.FirstPageAttemptLimit, _ => 1)
                .SetProperty(usage => usage.FirstPageAttemptUsed, _ => 1));

        var channel = Channel.CreateApprovedForScheduling(
            $"UC-{Guid.NewGuid():N}", "Overflow", null, "UU-overflow", Now, User.SystemUserId);

        var result = await CreateRepository().CreateApprovedForSchedulingAsync(
            channel,
            QuotaDate,
            CancellationToken.None);

        Assert.Equal(ChannelApprovalStatus.LimitReached, result.Status);
        Assert.Null(result.Channel);
        Assert.Equal(0, await dbContext.Channels.CountAsync(entity => entity.YoutubeChannelId == channel.YoutubeChannelId));

        var ledger = await dbContext.YouTubeDailyQuotaUsages.AsNoTracking().SingleAsync(entity => entity.QuotaDate == QuotaDate);
        Assert.Equal(1, ledger.FirstPageAttemptUsed);
        Assert.Equal(0, ledger.QueriesUnitsUsed);
    }

    [PostgresProofFact]
    public async Task Bulk_approval_rolls_back_every_selected_channel_and_counter_when_the_remaining_limit_is_too_small()
    {
        var channels = Enumerable.Range(1, 4)
            .Select(index => PendingChannel($"UC-bulk-{index}"))
            .ToArray();
        dbContext.Channels.AddRange(channels);
        await dbContext.SaveChangesAsync();

        await dbContext.YouTubeDailyQuotaUsages
            .Where(usage => usage.QuotaDate == QuotaDate)
            .ExecuteUpdateAsync(setters => setters
                .SetProperty(usage => usage.FirstPageAttemptLimit, _ => 100)
                .SetProperty(usage => usage.FirstPageAttemptUsed, _ => 97)
                .SetProperty(usage => usage.QueriesUnitsUsed, _ => 97)
                .SetProperty(usage => usage.VideoBatchStatsUnitsUsed, _ => 97));

        var result = await CreateRepository().TryApproveManyForSchedulingAsync(
            channels.Select(channel => channel.Id).ToArray(),
            QuotaDate,
            User.SystemUserId,
            Now,
            CancellationToken.None);

        Assert.Equal(ChannelBatchMutationStatus.LimitReached, result.Status);

        dbContext.ChangeTracker.Clear();
        var stored = await dbContext.Channels
            .AsNoTracking()
            .Where(channel => channels.Select(item => item.Id).Contains(channel.Id))
            .ToListAsync();
        Assert.All(stored, channel => Assert.Equal(ChannelStatus.PENDING, channel.Status));

        var ledger = await dbContext.YouTubeDailyQuotaUsages.AsNoTracking().SingleAsync(entity => entity.QuotaDate == QuotaDate);
        Assert.Equal(97, ledger.FirstPageAttemptUsed);
        Assert.Equal(97, ledger.QueriesUnitsUsed);
        Assert.Equal(97, ledger.VideoBatchStatsUnitsUsed);
    }

    [PostgresProofFact]
    public async Task A_manual_add_is_refused_when_the_queries_bucket_is_known_exhausted()
    {
        await MarkExhaustedAsync(queriesBucket: true);
        await AssertManualAddIsRefusedAsync();
    }

    [PostgresProofFact]
    public async Task A_manual_add_is_refused_when_the_batch_stats_bucket_is_known_exhausted()
    {
        await MarkExhaustedAsync(queriesBucket: false);
        await AssertManualAddIsRefusedAsync();
    }

    private async Task AssertManualAddIsRefusedAsync()
    {
        var channel = Channel.CreateApprovedForScheduling(
            $"UC-{Guid.NewGuid():N}", "Blocked", null, "UU-blocked", Now, User.SystemUserId);

        var result = await CreateRepository().CreateApprovedForSchedulingAsync(
            channel,
            QuotaDate,
            CancellationToken.None);

        Assert.Equal(ChannelApprovalStatus.LimitReached, result.Status);
        Assert.Null(result.Channel);
        Assert.Equal(0, await dbContext.Channels.CountAsync(entity => entity.YoutubeChannelId == channel.YoutubeChannelId));
        await AssertNoReservationAsync();
    }

    private YtFetcherDbContext CreateContext() =>
        new(new DbContextOptionsBuilder<YtFetcherDbContext>().UseNpgsql(connectionString).Options);

    private ChannelRepository CreateRepository() => new(dbContext);

    private static Channel PendingChannel(string youtubeChannelId) => new()
    {
        Id = Guid.NewGuid(),
        YoutubeChannelId = youtubeChannelId,
        Name = youtubeChannelId,
        Status = ChannelStatus.PENDING,
        BackfillStatus = BackfillStatus.PENDING,
        IsDeleted = false,
        DiscoveredAt = Now,
        CreatedAt = Now,
        ModifiedAt = Now
    };

    private async Task SeedWindowAsync(int firstPageAttemptLimit)
    {
        dbContext.YouTubeDailyQuotaUsages.Add(new YouTubeDailyQuotaUsage
        {
            Id = Guid.NewGuid(),
            QuotaDate = QuotaDate,
            FirstPageAttemptLimit = firstPageAttemptLimit,
            FirstPageAttemptUsed = 0,
            IsDeleted = false,
            CreatedAt = Now,
            ModifiedAt = Now
        });

        await dbContext.SaveChangesAsync();
    }

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

    private async Task AssertNoReservationAsync()
    {
        var ledger = await dbContext.YouTubeDailyQuotaUsages.AsNoTracking().SingleAsync(entity => entity.QuotaDate == QuotaDate);
        Assert.Equal(0, ledger.FirstPageAttemptUsed);
        Assert.Equal(0, ledger.QueriesUnitsUsed);
        Assert.Equal(0, ledger.VideoBatchStatsUnitsUsed);
    }
}
