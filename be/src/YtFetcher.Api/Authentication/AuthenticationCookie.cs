using Microsoft.AspNetCore.Authentication;
using YtFetcher.Application.Authentication.DTO.Result;

namespace YtFetcher.Api.Authentication;

/// <summary>
/// Transport for the internal application session. The token only ever travels in this host-only HttpOnly
/// cookie, so no JavaScript can read it, no JSON body ever carries it, and clearing it uses the same name,
/// path, and security attributes it was written with.
/// </summary>
internal static class AuthenticationCookie
{
    /// <summary>Stable application-specific cookie name; deliberately not the generic <c>token</c>.</summary>
    internal const string Name = "ytfetcher_session";

    private const string CookiePath = "/";

    internal static void Append(HttpResponse response, IssuedSessionToken session) =>
        response.Cookies.Append(Name, session.Token, CreateOptions(session.ExpiresAtUtc));

    internal static void Delete(HttpResponse response) =>
        response.Cookies.Delete(Name, CreateOptions(expiresAt: null));

    private static CookieOptions CreateOptions(DateTimeOffset? expiresAt) => new()
    {
        HttpOnly = true,
        Secure = true,
        SameSite = SameSiteMode.Lax,
        Path = CookiePath,
        IsEssential = true,
        // No Domain attribute: the cookie stays host-only for the API host.
        Expires = expiresAt
    };
}
