using YtFetcher.Application.Settings.DTO;

namespace YtFetcher.Application.Abstractions.UseCases;

public interface IUpdateGeneralSettingsUseCase
{
    Task<UpdateGeneralSettingsResult> UpdateAsync(
        UpdateGeneralSettingsRequest request,
        CancellationToken cancellationToken);
}
