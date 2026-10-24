using Microsoft.EntityFrameworkCore;
using YtFetcher.Application.Abstractions.Services;
using YtFetcher.Application.Backfill.DTO.Result;
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
/// PostgreSQL-only proofs for durable daily ownership and the per-bucket reservations. The claim decision, the
/// capacity predicates, and the finalization gate are decided by SQL predicates and row locks, so none of this
/// can be proven from an EF InMemory run. Skipped unless the runner supplies <c>YTF_TEST_POSTGRES</c>.
/// </summary>
[Collection(PostgresProofCollection.Name)]
public class DailyProcessingOwnershipPostgresTests : IAsyncLifetime
{
    private static readonly DateTimeOffset Now = new(2026, 10, 6, 8, 0, 0, TimeSpan.Zero);

    private static readonly DateOnly QuotaDate = new(2026, 10, 6);

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
    public async Task Two_simultaneous_daily_process_claim_attempts_yield_exactly_one_owner()
    {
        await EnsureLedgerAsync();

        var firstToken = Guid.NewGuid();
        var secondToken = Guid.NewGuid();
        await using var competitor = CreateContext();

        var outcomes = await Task.WhenAll(
            CreateQuotaRepository().TryClaimDailyProcessAsync(QuotaDate, firstToken, Now, CancellationToken.None),
            new DailyQuotaRepository(competitor, new StubAppSettingsService(), TimeProvider.System)
                .TryClaimDailyProcessAsync(QuotaDate, secondToken, Now, CancellationToken.None));

        Assert.Single(outcomes, outcome => outcome == DailyProcessClaimDecision.Claimed);
        Assert.Single(outcomes, outcome => outcome == DailyProcessClaimDecision.AlreadyRunning);

        var stored = await ReadLedgerAsync();
        Assert.Contains(stored.DailyProcessingClaimToken!.Value, new[] { firstToken, secondToken });
    }

    [PostgresProofFact]
    public async Task Phase_zero_snapshots_the_first_page_limit_without_consuming_a_slot()
    {
        await EnsureLedgerAsync();

        var ledger = await ReadLedgerAsync();

        Assert.Equal(500, ledger.FirstPageAttemptLimit);
        Assert.Equal(0, ledger.FirstPageAttemptUsed);
        Assert.True(ledger.IsFirstPageWindowReady);
    }

    [PostgresProofFact]
    public async Task A_queries_reservation_race_with_one_remaining_unit_yields_one_winner()
    {
        await EnsureLedgerAsync();

        // With 500 unclaimed first-page slots protected, 9499 ordinary Queries units leave exactly one.
        await dbContext.Database.ExecuteSqlInterpolatedAsync(
            $"UPDATE youtube_daily_quota_usage SET queries_units_used = 9499 WHERE quota_date = {QuotaDate}");

        await using var competitor = CreateContext();
        var outcomes = await Task.WhenAll(
            CreateQuotaRepository().TryReserveQueriesUnitAsync(QuotaDate, null, CancellationToken.None),
            new DailyQuotaRepository(competitor, new StubAppSettingsService(), TimeProvider.System)
                .TryReserveQueriesUnitAsync(QuotaDate, null, CancellationToken.None));

        Assert.Single(outcomes, outcome => outcome == QuotaReservationResult.Reserved);
        Assert.Single(outcomes, outcome => outcome == QuotaReservationResult.NoRemainingCapacity);
        Assert.Equal(9500, (await ReadLedgerAsync()).QueriesUnitsUsed);
    }

    [PostgresProofFact]
    public async Task The_two_buckets_are_independent_when_one_is_exhausted()
    {
        await EnsureLedgerAsync();

        await CreateQuotaRepository().MarkQueriesQuotaExhaustedAsync(QuotaDate, Now, CancellationToken.None);

        Assert.Equal(
            QuotaReservationResult.QuotaExhausted,
            await CreateQuotaRepository().TryReserveQueriesUnitAsync(QuotaDate, null, CancellationToken.None));

        // Video Batch Stats is untouched and still reservable.
        Assert.Equal(
            QuotaReservationResult.Reserved,
            await CreateQuotaRepository().TryReserveVideoBatchStatsUnitAsync(QuotaDate, null, CancellationToken.None));

        var ledger = await ReadLedgerAsync();
        Assert.True(ledger.IsQueriesQuotaExhausted);
        Assert.False(ledger.IsVideoBatchStatsQuotaExhausted);
        Assert.Equal(1, ledger.VideoBatchStatsUnitsUsed);
    }

    [PostgresProofFact]
    public async Task A_reservation_before_phase_zero_reports_the_window_is_not_ready()
    {
        // No EnsureLedgerAsync: the date row does not exist.
        Assert.Equal(
            QuotaReservationResult.WindowNotReady,
            await CreateQuotaRepository().TryReserveQueriesUnitAsync(QuotaDate, null, CancellationToken.None));
    }

    [PostgresProofFact]
    public async Task Finalization_opens_the_gate_only_when_both_remainders_are_positive()
    {
        await EnsureLedgerAsync();
        var token = Guid.NewGuid();
        await CreateQuotaRepository().TryClaimDailyProcessAsync(QuotaDate, token, Now, CancellationToken.None);

        var opened = await CreateQuotaRepository().FinalizeDailyProcessingAsync(
            QuotaDate,
            token,
            Now,
            CancellationToken.None);

        Assert.True(opened.Finalized);
        Assert.True(opened.GateOpened);
        Assert.Equal(10000 - 500, opened.QueriesRemainder);
        Assert.Equal(10000 - 500, opened.VideoBatchStatsRemainder);
        Assert.True((await ReadLedgerAsync()).IsBackfillDispatchOpen);
    }

    [PostgresProofFact]
    public async Task Finalization_does_not_open_the_gate_when_a_bucket_is_exhausted()
    {
        await EnsureLedgerAsync();
        var token = Guid.NewGuid();
        await CreateQuotaRepository().TryClaimDailyProcessAsync(QuotaDate, token, Now, CancellationToken.None);
        await CreateQuotaRepository().MarkVideoBatchStatsQuotaExhaustedAsync(QuotaDate, Now, CancellationToken.None);

        var finalized = await CreateQuotaRepository().FinalizeDailyProcessingAsync(
            QuotaDate,
            token,
            Now,
            CancellationToken.None);

        Assert.True(finalized.Finalized);
        Assert.False(finalized.GateOpened);
        Assert.False((await ReadLedgerAsync()).IsBackfillDispatchOpen);
    }

    [PostgresProofFact]
    public async Task A_completed_date_is_never_claimed_again()
    {
        await EnsureLedgerAsync();
        var token = Guid.NewGuid();
        await CreateQuotaRepository().TryClaimDailyProcessAsync(QuotaDate, token, Now, CancellationToken.None);
        await CreateQuotaRepository().FinalizeDailyProcessingAsync(QuotaDate, token, Now, CancellationToken.None);

        Assert.Equal(
            DailyProcessClaimDecision.AlreadyCompleted,
            await CreateQuotaRepository().TryClaimDailyProcessAsync(QuotaDate, Guid.NewGuid(), Now, CancellationToken.None));
    }

    [PostgresProofFact]
    public async Task M7_persistence_locks_the_ledger_and_persists_a_claimed_batch()
    {
        var (video, dailyToken, videoToken) = await SeedClaimedVideoAsync();

        var result = await CreateStatisticsRepository().PersistSuccessAsync(
            QuotaDate,
            dailyToken,
            [video.Id],
            videoToken,
            [new VideoStatsItemResult(video.YoutubeVideoId, 30_000, 1_500, 70)],
            Now,
            CancellationToken.None);

        // If the ledger lock still referenced the dropped legacy column this call could not succeed at all.
        Assert.True(result.OwnershipRetained);
        Assert.Equal(1, result.Updated);

        var stored = await dbContext.Videos.AsNoTracking().SingleAsync(entity => entity.Id == video.Id);
        Assert.Equal(1_500, stored.CurrentViews);
        Assert.Null(stored.StatsClaimToken);
        Assert.Equal(1, await dbContext.VideoSnapshots.CountAsync(snapshot => snapshot.VideoId == video.Id));
    }

    [PostgresProofFact]
    public async Task M7_persistence_discards_a_stale_daily_token_and_keeps_its_claims()
    {
        var (video, dailyToken, videoToken) = await SeedClaimedVideoAsync();
        await CreateQuotaRepository().ReleaseDailyProcessClaimAsync(QuotaDate, dailyToken, CancellationToken.None);
        await CreateQuotaRepository().TryClaimDailyProcessAsync(QuotaDate, Guid.NewGuid(), Now, CancellationToken.None);

        var result = await CreateStatisticsRepository().PersistSuccessAsync(
            QuotaDate,
            dailyToken,
            [video.Id],
            videoToken,
            [new VideoStatsItemResult(video.YoutubeVideoId, 30_000, 1_500, 70)],
            Now,
            CancellationToken.None);

        Assert.False(result.OwnershipRetained);
        Assert.Equal(0, await dbContext.VideoSnapshots.CountAsync(snapshot => snapshot.VideoId == video.Id));

        // The video claim is untouched so its rightful owner can still release it with a token-matched call.
        var stored = await dbContext.Videos.AsNoTracking().SingleAsync(entity => entity.Id == video.Id);
        Assert.Equal(videoToken, stored.StatsClaimToken);
    }

    private YtFetcherDbContext CreateContext() =>
        new(new DbContextOptionsBuilder<YtFetcherDbContext>().UseNpgsql(connectionString).Options);

    private DailyQuotaRepository CreateQuotaRepository() =>
        new(dbContext, new StubAppSettingsService(), TimeProvider.System);

    private async Task EnsureLedgerAsync() =>
        await CreateQuotaRepository().EnsureLedgerAsync(QuotaDate, CancellationToken.None);

    private VideoStatisticsRepository CreateStatisticsRepository() => new(dbContext);

    /// <summary>Seeds one active tracked video already claimed for statistics under the current daily owner.</summary>
    private async Task<(Video Video, Guid DailyToken, Guid VideoToken)> SeedClaimedVideoAsync()
    {
        await EnsureLedgerAsync();
        var dailyToken = Guid.NewGuid();
        await CreateQuotaRepository().TryClaimDailyProcessAsync(QuotaDate, dailyToken, Now, CancellationToken.None);

        var channel = new Channel
        {
            Id = Guid.NewGuid(),
            YoutubeChannelId = $"UC-{Guid.NewGuid():N}",
            Name = "M7 proof channel",
            Status = ChannelStatus.APPROVED,
            BackfillStatus = BackfillStatus.COMPLETED,
            IsDeleted = false,
            DiscoveredAt = Now,
            CreatedAt = Now,
            ModifiedAt = Now
        };
        dbContext.Channels.Add(channel);

        var videoToken = Guid.NewGuid();
        var video = new Video
        {
            Id = Guid.NewGuid(),
            YoutubeVideoId = Guid.NewGuid().ToString("N"),
            ChannelId = channel.Id,
            Title = "M7 proof video",
            PublishedAt = Now.AddDays(-2),
            DurationMillis = 30_000,
            CurrentViews = 10,
            CurrentLikes = 1,
            IsDeleted = false,
            CreatedAt = Now,
            ModifiedAt = Now
        };
        video.ClaimForStatistics(videoToken, Now);
        dbContext.Videos.Add(video);

        await dbContext.SaveChangesAsync();
        return (video, dailyToken, videoToken);
    }

    private Task<YouTubeDailyQuotaUsage> ReadLedgerAsync() =>
        dbContext.YouTubeDailyQuotaUsages
            .AsNoTracking()
            .SingleAsync(row => row.QuotaDate == QuotaDate);

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
