using System.Text.Encodings.Web;
using System.Security.Claims;
using Microsoft.AspNetCore.Authentication;
using Microsoft.Extensions.Options;
using YtFetcher.Application.Abstractions.Services;

namespace YtFetcher.Api.Authentication;

/// <summary>
/// Reads the internal session JWT from the HttpOnly cookie and authenticates the request with it. It is
/// registered only while authentication is enabled, and it authenticates nothing without a valid token, so a
/// missing, forged, foreign, or expired cookie stays an anonymous request.
/// </summary>
internal sealed class SessionCookieAuthenticationHandler(
    IOptionsMonitor<AuthenticationSchemeOptions> options,
    ILoggerFactory logger,
    UrlEncoder encoder,
    IApplicationSessionTokenValidator tokenValidator)
    : AuthenticationHandler<AuthenticationSchemeOptions>(options, logger, encoder)
{
    protected override async Task<AuthenticateResult> HandleAuthenticateAsync()
    {
        if (!Request.Cookies.TryGetValue(AuthenticationCookie.Name, out var token) ||
            string.IsNullOrWhiteSpace(token))
        {
            return AuthenticateResult.NoResult();
        }

        var identity = await tokenValidator.ValidateAsync(token, Context.RequestAborted);

        return identity is null
            ? AuthenticateResult.Fail("The session cookie is not valid.")
            : AuthenticateResult.Success(new AuthenticationTicket(
                new ClaimsPrincipal(new ClaimsIdentity(
                [
                    new Claim(ClaimTypes.NameIdentifier, identity.UserId.ToString()),
                    new Claim(ClaimTypes.Email, identity.Email),
                    new Claim(ClaimTypes.Role, identity.Role)
                ],
                Scheme.Name,
                ClaimTypes.Email,
                ClaimTypes.Role)),
                Scheme.Name));
    }
}
