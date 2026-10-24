using Microsoft.Extensions.Hosting;

namespace YtFetcher.Api.Configuration;

/// <summary>
/// Decides whether the Hangfire Dashboard middleware is mapped. The dashboard is a local
/// development tool: it is mapped in Development only and never exposed in other environments.
/// </summary>
internal static class HangfireDashboardMapping
{
    internal const string Path = "/hangfire";

    internal static bool ShouldMap(IHostEnvironment environment) => environment.IsDevelopment();
}
