using System.Security.Claims;
using Microsoft.AspNetCore.Http;
using YtFetcher.Api.Authentication;
using YtFetcher.Core.Users;

namespace YtFetcher.Api.Tests.Authentication;

public class CurrentActorTests
{
    [Fact]
    public void Uses_the_verified_name_identifier_for_an_authenticated_request()
    {
        var userId = Guid.NewGuid();
        var accessor = CreateAccessor(isAuthenticated: true, userId.ToString());

        var actor = new CurrentActor(accessor);

        Assert.Equal(userId, actor.UserId);
    }

    [Theory]
    [InlineData(false, "00000000-0000-0000-0000-000000000002")]
    [InlineData(true, "not-a-guid")]
    [InlineData(true, "00000000-0000-0000-0000-000000000000")]
    public void Uses_the_system_actor_when_the_request_identity_is_not_usable(
        bool isAuthenticated,
        string? nameIdentifier)
    {
        var accessor = CreateAccessor(isAuthenticated, nameIdentifier);

        var actor = new CurrentActor(accessor);

        Assert.Equal(User.SystemUserId, actor.UserId);
    }

    private static IHttpContextAccessor CreateAccessor(bool isAuthenticated, string? nameIdentifier)
    {
        var claims = nameIdentifier is null
            ? Array.Empty<Claim>()
            : [new Claim(ClaimTypes.NameIdentifier, nameIdentifier)];
        var context = new DefaultHttpContext
        {
            User = new ClaimsPrincipal(new ClaimsIdentity(
                claims,
                isAuthenticated ? "verified-session" : null))
        };

        return new HttpContextAccessor { HttpContext = context };
    }
}
