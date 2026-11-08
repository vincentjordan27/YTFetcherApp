using YtFetcher.Application.Abstractions.Repositories;
using YtFetcher.Application.Abstractions.Services;
using YtFetcher.Application.Settings;

namespace YtFetcher.Infrastructure.Services;

public sealed class GeneralSettingsProvider(IGeneralSettingsRepository repository) : IGeneralSettingsProvider
{
    private Task<GeneralSettingsSnapshot>? settingsTask;

    public Task<GeneralSettingsSnapshot> GetRequiredAsync(CancellationToken cancellationToken) =>
        settingsTask ??= repository.GetRequiredAsync(cancellationToken);
}
