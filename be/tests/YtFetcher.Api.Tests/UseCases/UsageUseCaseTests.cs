using YtFetcher.Application.Abstractions.Repositories;
using YtFetcher.Application.Abstractions.Services;
using YtFetcher.Application.Quotas;
using YtFetcher.Application.Quotas.DTO.Result;
using YtFetcher.Application.Usage.UseCases;
using YtFetcher.Core.Quotas;

namespace YtFetcher.Api.Tests.UseCases;

/// <summary>
/// The Usage use case is a read-only projection of the three provider buckets: it returns each bucket's used
/// counter, its configured limit, the configured first-page cap, and the per-bucket exhaustion timestamps. It
/// never creates or reserves quota.
/// </summary>
public class UsageUseCaseTests
{
    [Fact]
    public async Task An_active_ledger_row_returns_bucket_usage_and_configured_limits()
    {
        var repository = new FakeDailyQuotaRepository
        {
            Ledger = new YouTubeDailyQuotaUsage
            {
                QuotaDate = new DateOnly(2026, 10, 1),
                QueriesUnitsUsed = 300,
                VideoBatchStatsUnitsUsed = 400,
                SearchQueryUnitsUsed = 12,
                FirstPageAttemptLimit = 500,
                FirstPageAttemptUsed = 40
            }
        };

        var useCase = CreateUseCase(repository, new DateTimeOffset(2026, 10, 1, 17, 30, 0, TimeSpan.Zero));

        var response = await useCase.GetTodayAsync(CancellationToken.None);

        Assert.Equal(new DateOnly(2026, 10, 1), response.QuotaDate);
        Assert.Equal(new DateOnly(2026, 10, 1), repository.ReceivedQuotaDate);
        Assert.True(response.HasLedger);

        Assert.Equal(300, response.QueriesUnitsUsed);
        Assert.Equal(10000, response.QueriesDailyQuotaLimit);
        Assert.Equal(400, response.VideoBatchStatsUnitsUsed);
        Assert.Equal(10000, response.VideoBatchGetStatsDailyQuotaLimit);
        Assert.Equal(12, response.SearchQueryUnitsUsed);
        Assert.Equal(100, response.SearchQueriesDailyQuotaLimit);

        // The cap comes from configuration, not from the frontend and not hardcoded.
        Assert.Equal(40, response.FirstPageAttemptUsed);
        Assert.Equal(500, response.MaxDailyApprovedChannels);
    }

    [Fact]
    public async Task A_missing_ledger_row_reports_the_pacific_date_with_no_ledger_and_configured_limits()
    {
        var repository = new FakeDailyQuotaRepository { Ledger = null };

        // 05:00 UTC is 22:00 on the previous day in Pacific time, so the quota date must be 2026-09-30.
        var useCase = CreateUseCase(repository, new DateTimeOffset(2026, 10, 1, 5, 0, 0, TimeSpan.Zero));

        var response = await useCase.GetTodayAsync(CancellationToken.None);

        Assert.Equal(new DateOnly(2026, 9, 30), response.QuotaDate);
        Assert.False(response.HasLedger);
        Assert.Equal(0, response.QueriesUnitsUsed);
        Assert.Equal(0, response.VideoBatchStatsUnitsUsed);
        Assert.Equal(0, response.SearchQueryUnitsUsed);
        Assert.Equal(0, response.FirstPageAttemptUsed);

        // Limits are configuration, so they are still reported.
        Assert.Equal(10000, response.QueriesDailyQuotaLimit);
        Assert.Equal(10000, response.VideoBatchGetStatsDailyQuotaLimit);
        Assert.Equal(100, response.SearchQueriesDailyQuotaLimit);
        Assert.Equal(500, response.MaxDailyApprovedChannels);
    }

    [Fact]
    public async Task Exhaustion_timestamps_are_projected_per_bucket()
    {
        var repository = new FakeDailyQuotaRepository
        {
            Ledger = new YouTubeDailyQuotaUsage
            {
                QuotaDate = new DateOnly(2026, 10, 1),
                FirstPageAttemptLimit = 500
            }
        };

        var useCase = CreateUseCase(repository, new DateTimeOffset(2026, 10, 1, 17, 30, 0, TimeSpan.Zero));
        await useCase.GetTodayAsync(CancellationToken.None);

        // The projection must not invent an exhaustion marker when none was set.
        var response = await useCase.GetTodayAsync(CancellationToken.None);
        Assert.Null(response.SearchQuotaExhaustedAt);
        Assert.Null(response.QueriesQuotaExhaustedAt);
        Assert.Null(response.VideoBatchStatsQuotaExhaustedAt);
    }

    [Fact]
    public async Task An_initialized_day_reports_its_persisted_first_page_cap_after_a_config_change()
    {
        var repository = new FakeDailyQuotaRepository
        {
            Ledger = new YouTubeDailyQuotaUsage
            {
                QuotaDate = new DateOnly(2026, 10, 1),
                FirstPageAttemptLimit = 321,
                FirstPageAttemptUsed = 4
            }
        };

        var useCase = CreateUseCase(repository, new DateTimeOffset(2026, 10, 1, 17, 30, 0, TimeSpan.Zero));

        var response = await useCase.GetTodayAsync(CancellationToken.None);

        // The configured stub says 500, but an initialized day must report the cap it was created with.
        Assert.Equal(321, response.MaxDailyApprovedChannels);
        Assert.Equal(4, response.FirstPageAttemptUsed);
    }

    private static UsageUseCase CreateUseCase(IDailyQuotaRepository repository, DateTimeOffset utcNow) =>
        new(repository, new StubAppSettingsService(), new FixedTimeProvider(utcNow));

    private sealed class FixedTimeProvider(DateTimeOffset utcNow) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => utcNow;
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

    private sealed class FakeDailyQuotaRepository : IDailyQuotaRepository
    {
        public YouTubeDailyQuotaUsage? Ledger { get; init; }

        public DateOnly? ReceivedQuotaDate { get; private set; }

        public Task<YouTubeDailyQuotaUsage?> GetForDateAsync(DateOnly quotaDate, CancellationToken cancellationToken)
        {
            ReceivedQuotaDate = quotaDate;
            return Task.FromResult(Ledger);
        }

        public Task EnsureLedgerAsync(DateOnly quotaDate, CancellationToken cancellationToken) => Task.CompletedTask;

        public Task<DailyProcessClaimDecision> TryClaimDailyProcessAsync(
            DateOnly quotaDate,
            Guid claimToken,
            DateTimeOffset utcNow,
            CancellationToken cancellationToken) =>
            Task.FromResult(DailyProcessClaimDecision.AlreadyRunning);

        public Task ReleaseDailyProcessClaimAsync(DateOnly quotaDate, Guid claimToken, CancellationToken cancellationToken) =>
            Task.CompletedTask;

        public Task<DailyFinalizationResult> FinalizeDailyProcessingAsync(
            DateOnly quotaDate,
            Guid claimToken,
            DateTimeOffset utcNow,
            CancellationToken cancellationToken) =>
            Task.FromResult(new DailyFinalizationResult(false, false, 0, 0));

        public Task<QuotaReservationResult> TryReserveQueriesUnitAsync(DateOnly quotaDate, Guid? dailyProcessClaimToken, CancellationToken cancellationToken) =>
            Task.FromResult(QuotaReservationResult.NoRemainingCapacity);

        public Task<QuotaReservationResult> TryReserveVideoBatchStatsUnitAsync(DateOnly quotaDate, Guid? dailyProcessClaimToken, CancellationToken cancellationToken) =>
            Task.FromResult(QuotaReservationResult.NoRemainingCapacity);

        public Task<QuotaReservationResult> TryReserveSearchUnitAsync(DateOnly quotaDate, Guid? dailyProcessClaimToken, CancellationToken cancellationToken) =>
            Task.FromResult(QuotaReservationResult.NoRemainingCapacity);

        public Task MarkSearchQuotaExhaustedAsync(DateOnly quotaDate, DateTimeOffset utcNow, CancellationToken cancellationToken) =>
            Task.CompletedTask;

        public Task MarkQueriesQuotaExhaustedAsync(DateOnly quotaDate, DateTimeOffset utcNow, CancellationToken cancellationToken) =>
            Task.CompletedTask;

        public Task MarkVideoBatchStatsQuotaExhaustedAsync(DateOnly quotaDate, DateTimeOffset utcNow, CancellationToken cancellationToken) =>
            Task.CompletedTask;
    }
}
