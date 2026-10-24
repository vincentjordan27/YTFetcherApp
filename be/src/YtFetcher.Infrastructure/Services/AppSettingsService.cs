using Microsoft.Extensions.Configuration;
using YtFetcher.Application.Abstractions.Services;

namespace YtFetcher.Infrastructure.Services;

public sealed class AppSettingsService(IConfiguration configuration) : IAppSettingsService
{
    public string? GetValue(string key) => configuration[key];

    public string GetRequiredValue(string key) =>
        GetValue(key) is { Length: > 0 } value
            ? value
            : throw new InvalidOperationException($"Required configuration value '{key}' is missing.");

    public T GetRequiredSection<T>(string sectionName) where T : class =>
        configuration.GetSection(sectionName).Get<T>()
        ?? throw new InvalidOperationException($"Required configuration section '{sectionName}' is missing.");
}
