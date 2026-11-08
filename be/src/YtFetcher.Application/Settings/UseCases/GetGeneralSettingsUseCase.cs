using YtFetcher.Application.Abstractions.Services;
using YtFetcher.Application.Abstractions.UseCases;
using YtFetcher.Application.Settings.DTO;

namespace YtFetcher.Application.Settings.UseCases;

public sealed class GetGeneralSettingsUseCase(IGeneralSettingsProvider settingsProvider) : IGetGeneralSettingsUseCase
{
    public async Task<GeneralSettingsResponse> GetAsync(CancellationToken cancellationToken) =>
        ToResponse(await settingsProvider.GetRequiredAsync(cancellationToken));

    internal static GeneralSettingsResponse ToResponse(GeneralSettingsSnapshot settings) =>
        new(
            settings.QueriesDailyQuotaLimit,
            settings.VideoBatchGetStatsDailyQuotaLimit,
            settings.SearchQueriesDailyQuotaLimit,
            settings.DiscoveryMaxPagesPerKeywordPerRun,
            settings.MaxDailyApprovedChannels,
            settings.BackfillPageLimit,
            settings.BackfillDispatchBatchSize,
            settings.MinimumSubscriberCount);
}
