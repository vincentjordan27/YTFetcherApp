using YtFetcher.Application.Abstractions.UseCases;

namespace YtFetcher.Api.Endpoints.Usage;

public static class UsageEndpoints
{
    public static IEndpointRouteBuilder MapUsageEndpoints(this IEndpointRouteBuilder endpoints)
    {
        // Read-only: no body, no path or query input, and no validator. An absent daily row is a
        // normal state carried by hasLedger, never a 404.
        endpoints.MapGet("/api/usage/today", async (
            HttpResponse response,
            IUsageUseCase usageUseCase,
            CancellationToken cancellationToken) =>
        {
            // The ledger is live mutable state. A tab must never reuse an older GET response.
            response.Headers.CacheControl = "no-store";
            return Results.Ok(await usageUseCase.GetTodayAsync(cancellationToken));
        });

        return endpoints;
    }
}
