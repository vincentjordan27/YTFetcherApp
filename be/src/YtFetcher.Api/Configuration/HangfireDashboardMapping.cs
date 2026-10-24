namespace YtFetcher.Api.Configuration;

/// <summary>
/// Decides whether the Hangfire Dashboard middleware is mapped. The explicit operator setting is the only
/// mapping switch. Application authentication adds a session requirement when it is enabled; otherwise,
/// the operator accepts that the mapped dashboard has no application-level authentication.
/// </summary>
internal static class HangfireDashboardMapping
{
    internal const string Path = "/hangfire";

    internal static bool ShouldMap(bool dashboardEnabled) => dashboardEnabled;
}
