using YtFetcher.Application.Abstractions.Repositories;
using YtFetcher.Application.Abstractions.Services;
using YtFetcher.Application.Abstractions.UseCases;
using YtFetcher.Application.Quotas;
using YtFetcher.Application.Usage.DTO.Response;

namespace YtFetcher.Application.Usage.UseCases;

/// <summary>
/// Read-only view of the current Pacific calendar day's provider quota buckets. It reads the row the daily
/// module already persists and owns the Pacific date conversion; it never creates, reserves, or resets quota.
/// The configured limits come from the existing settings port so the frontend never hardcodes them.
/// </summary>
public sealed class UsageUseCase(
    IDailyQuotaRepository dailyQuotaRepository,
    IGeneralSettingsProvider generalSettingsProvider,
    TimeProvider timeProvider) : IUsageUseCase
{
    public async Task<TodayUsageResponse> GetTodayAsync(CancellationToken cancellationToken)
    {
        var quotaDate = PacificQuotaWindow.GetQuotaDate(timeProvider.GetUtcNow());

        var ledger = await dailyQuotaRepository.GetForDateAsync(quotaDate, cancellationToken);

        var settings = await generalSettingsProvider.GetRequiredAsync(cancellationToken);

        // The first-page cap is a persisted per-day snapshot: an initialized day must report the limit it was
        // created with, not whatever General Settings says later. Only a no-ledger day reads settings.
        var maxApprovedChannels = ledger?.FirstPageAttemptLimit
            ?? settings.MaxDailyApprovedChannels;

        return new TodayUsageResponse(
            quotaDate,
            ledger is not null,
            ledger?.QueriesUnitsUsed ?? 0,
            settings.QueriesDailyQuotaLimit,
            ledger?.VideoBatchStatsUnitsUsed ?? 0,
            settings.VideoBatchGetStatsDailyQuotaLimit,
            ledger?.SearchQueryUnitsUsed ?? 0,
            settings.SearchQueriesDailyQuotaLimit,
            ledger?.FirstPageAttemptUsed ?? 0,
            maxApprovedChannels,
            ledger?.SearchQuotaExhaustedAt,
            ledger?.QueriesQuotaExhaustedAt,
            ledger?.VideoBatchStatsQuotaExhaustedAt);
    }
}
