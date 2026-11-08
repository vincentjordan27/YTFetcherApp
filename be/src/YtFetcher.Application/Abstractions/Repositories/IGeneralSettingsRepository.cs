using YtFetcher.Application.Settings;

namespace YtFetcher.Application.Abstractions.Repositories;

public interface IGeneralSettingsRepository
{
    Task<GeneralSettingsSnapshot> GetRequiredAsync(CancellationToken cancellationToken);

    Task<GeneralSettingsSnapshot> UpdateAsync(
        GeneralSettingsSnapshot settings,
        Guid? actorUserId,
        CancellationToken cancellationToken);
}
