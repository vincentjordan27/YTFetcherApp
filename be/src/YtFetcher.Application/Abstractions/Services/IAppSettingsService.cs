namespace YtFetcher.Application.Abstractions.Services;

public interface IAppSettingsService
{
    string? GetValue(string key);

    string GetRequiredValue(string key);

    T GetRequiredSection<T>(string sectionName) where T : class;
}
