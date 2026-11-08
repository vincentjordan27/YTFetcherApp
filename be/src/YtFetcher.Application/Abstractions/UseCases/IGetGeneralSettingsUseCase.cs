using YtFetcher.Application.Settings.DTO;

namespace YtFetcher.Application.Abstractions.UseCases;

public interface IGetGeneralSettingsUseCase
{
    Task<GeneralSettingsResponse> GetAsync(CancellationToken cancellationToken);
}
