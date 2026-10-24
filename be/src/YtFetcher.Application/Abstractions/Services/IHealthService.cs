using YtFetcher.Application.Health.DTO.Response;

namespace YtFetcher.Application.Abstractions.Services;

public interface IHealthService
{
    HealthResponse GetStatus();
}
