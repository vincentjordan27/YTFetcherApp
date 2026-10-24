using YtFetcher.Application.Abstractions.Services;
using YtFetcher.Application.Health.DTO.Response;

namespace YtFetcher.Infrastructure.Health;

public sealed class HealthService(IAppSettingsService appSettingsService) : IHealthService
{
    public HealthResponse GetStatus() => new(
        "ok",
        appSettingsService.GetValue("Service:Name") ?? "yt-fetcher");
}
