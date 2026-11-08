using YtFetcher.Application.Settings;

namespace YtFetcher.Application.Abstractions.Services;

public interface IGeneralSettingsProvider
{
    Task<GeneralSettingsSnapshot> GetRequiredAsync(CancellationToken cancellationToken);
}
