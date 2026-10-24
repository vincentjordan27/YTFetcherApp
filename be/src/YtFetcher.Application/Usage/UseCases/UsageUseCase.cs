using System.Globalization;
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
    IAppSettingsService appSettingsService,
    TimeProvider timeProvider) : IUsageUseCase
{
    public async Task<TodayUsageResponse> GetTodayAsync(CancellationToken cancellationToken)
    {
        var quotaDate = PacificQuotaWindow.GetQuotaDate(timeProvider.GetUtcNow());

        var ledger = await dailyQuotaRepository.GetForDateAsync(quotaDate, cancellationToken);

        var queriesLimit = ReadRequiredInt(QuotaConfigurationKeys.QueriesDailyQuotaLimit);
        var batchStatsLimit = ReadRequiredInt(QuotaConfigurationKeys.VideoBatchGetStatsDailyQuotaLimit);
        var searchLimit = ReadRequiredInt(QuotaConfigurationKeys.SearchQueriesDailyQuotaLimit);

        // The first-page cap is a persisted per-day snapshot: an initialized day must report the limit it was
        // created with, not whatever configuration says later. Only a no-ledger day reads configuration.
        var maxApprovedChannels = ledger?.FirstPageAttemptLimit
            ?? ReadRequiredInt(QuotaConfigurationKeys.MaxDailyApprovedChannels);

        return new TodayUsageResponse(
            quotaDate,
            ledger is not null,
            ledger?.QueriesUnitsUsed ?? 0,
            queriesLimit,
            ledger?.VideoBatchStatsUnitsUsed ?? 0,
            batchStatsLimit,
            ledger?.SearchQueryUnitsUsed ?? 0,
            searchLimit,
            ledger?.FirstPageAttemptUsed ?? 0,
            maxApprovedChannels,
            ledger?.SearchQuotaExhaustedAt,
            ledger?.QueriesQuotaExhaustedAt,
            ledger?.VideoBatchStatsQuotaExhaustedAt);
    }

    private int ReadRequiredInt(string key)
    {
        var value = appSettingsService.GetValue(key);

        return int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsed)
            ? parsed
            : throw new InvalidOperationException(
                $"Configuration value '{key}' is missing or not a whole number.");
    }
}
