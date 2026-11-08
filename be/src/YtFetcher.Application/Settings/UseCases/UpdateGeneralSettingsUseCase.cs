using YtFetcher.Application.Abstractions.Repositories;
using YtFetcher.Application.Abstractions.Services;
using YtFetcher.Application.Abstractions.UseCases;
using YtFetcher.Application.Settings.DTO;

namespace YtFetcher.Application.Settings.UseCases;

public sealed class UpdateGeneralSettingsUseCase(
    IGeneralSettingsRepository settingsRepository,
    ICurrentActor currentActor) : IUpdateGeneralSettingsUseCase
{
    public async Task<UpdateGeneralSettingsResult> UpdateAsync(
        UpdateGeneralSettingsRequest request,
        CancellationToken cancellationToken)
    {
        var errors = Validate(request);
        if (errors.Count != 0)
        {
            return new UpdateGeneralSettingsResult(null, errors);
        }

        var updated = await settingsRepository.UpdateAsync(
            new GeneralSettingsSnapshot(
                request.QueriesDailyQuotaLimit,
                request.VideoBatchGetStatsDailyQuotaLimit,
                request.SearchQueriesDailyQuotaLimit,
                request.DiscoveryMaxPagesPerKeywordPerRun,
                request.MaxDailyApprovedChannels,
                request.BackfillPageLimit,
                request.BackfillDispatchBatchSize,
                request.MinimumSubscriberCount),
            currentActor.UserId,
            cancellationToken);

        return new UpdateGeneralSettingsResult(GetGeneralSettingsUseCase.ToResponse(updated), null);
    }

    private static List<GeneralSettingsValidationError> Validate(UpdateGeneralSettingsRequest request)
    {
        var errors = new List<GeneralSettingsValidationError>();
        AddAtLeastOne(errors, "queriesDailyQuotaLimit", request.QueriesDailyQuotaLimit);
        AddAtLeastOne(errors, "videoBatchGetStatsDailyQuotaLimit", request.VideoBatchGetStatsDailyQuotaLimit);
        AddAtLeastOne(errors, "searchQueriesDailyQuotaLimit", request.SearchQueriesDailyQuotaLimit);
        AddAtLeastZero(errors, "discoveryMaxPagesPerKeywordPerRun", request.DiscoveryMaxPagesPerKeywordPerRun);
        AddAtLeastOne(errors, "maxDailyApprovedChannels", request.MaxDailyApprovedChannels);
        AddAtLeastZero(errors, "backfillPageLimit", request.BackfillPageLimit);
        AddAtLeastOne(errors, "backfillDispatchBatchSize", request.BackfillDispatchBatchSize);
        if (request.MinimumSubscriberCount is < 0)
        {
            AddError(errors, "minimumSubscriberCount", "Minimum subscriber count must be zero or more.");
        }

        return errors;
    }

    private static void AddAtLeastOne(List<GeneralSettingsValidationError> errors, string field, int value)
    {
        if (value < 1)
        {
            AddError(errors, field, "Value must be at least 1.");
        }
    }

    private static void AddAtLeastZero(List<GeneralSettingsValidationError> errors, string field, int value)
    {
        if (value < 0)
        {
            AddError(errors, field, "Value must be zero or more.");
        }
    }

    private static void AddError(List<GeneralSettingsValidationError> errors, string field, string message) =>
        errors.Add(new GeneralSettingsValidationError(field, message));
}
