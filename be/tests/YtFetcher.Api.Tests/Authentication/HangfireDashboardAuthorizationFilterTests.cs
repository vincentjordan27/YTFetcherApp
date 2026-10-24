using System.Security.Claims;
using Microsoft.AspNetCore.Http;
using YtFetcher.Api.Authentication;

namespace YtFetcher.Api.Tests.Authentication;

/// <summary>
/// The Development dashboard keeps its local-only rule and, while authentication is enabled, additionally
/// requires the session principal the authentication middleware established.
/// </summary>
public class HangfireDashboardAuthorizationFilterTests
{
    [Fact]
    public void An_authenticated_session_principal_is_allowed()
    {
        var accessor = new HttpContextAccessor
        {
            HttpContext = new DefaultHttpContext
            {
                User = new ClaimsPrincipal(new ClaimsIdentity([new Claim("sub", Guid.NewGuid().ToString())], "test"))
            }
        };

        Assert.True(new HangfireDashboardAuthorizationFilter(accessor).Authorize(context: null!));
    }

    [Fact]
    public void An_anonymous_or_missing_request_is_denied()
    {
        var anonymous = new HttpContextAccessor { HttpContext = new DefaultHttpContext() };
        var missing = new HttpContextAccessor();

        Assert.False(new HangfireDashboardAuthorizationFilter(anonymous).Authorize(context: null!));
        Assert.False(new HangfireDashboardAuthorizationFilter(missing).Authorize(context: null!));
    }
}
