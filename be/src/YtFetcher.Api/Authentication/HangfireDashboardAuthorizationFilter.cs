using Hangfire.Dashboard;

namespace YtFetcher.Api.Authentication;

/// <summary>
/// Requires the internal session cookie before the Development Hangfire Dashboard is served while
/// authentication is enabled. It reuses the request principal the authentication middleware already
/// established, so it never validates a token itself.
/// </summary>
internal sealed class HangfireDashboardAuthorizationFilter(IHttpContextAccessor httpContextAccessor)
    : IDashboardAuthorizationFilter
{
    public bool Authorize(DashboardContext context)
    {
        var httpContext = httpContextAccessor.HttpContext;

        if (httpContext?.User.Identity?.IsAuthenticated is not true)
        {
            return false;
        }

        return true;
    }
}
