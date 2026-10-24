using YtFetcher.Application.Abstractions.Services;

namespace YtFetcher.Api.Endpoints.Health;

public static class HealthEndpoints
{
    public static IEndpointRouteBuilder MapHealthEndpoints(this IEndpointRouteBuilder endpoints)
    {
        // Health stays anonymous in both authentication modes and reports no configuration, identity, quota,
        // or operational detail.
        endpoints.MapGet("/api/health", (IHealthService healthService) =>
            Results.Ok(healthService.GetStatus())).AllowAnonymous();

        return endpoints;
    }
}
