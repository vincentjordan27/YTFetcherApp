using YtFetcher.Application.Authentication.DTO.Response;

namespace YtFetcher.Application.Authentication.DTO.Result;

/// <summary>
/// Outcome of exchanging a Firebase ID token for an application session. The endpoint maps it to an HTTP
/// status and writes the cookie; it never becomes part of a response body directly.
/// </summary>
public sealed record CreateAuthenticationSessionResult(
    CreateAuthenticationSessionStatus Status,
    AuthenticationIdentityDto? Identity,
    IssuedSessionToken? Session)
{
    public static CreateAuthenticationSessionResult Disabled() =>
        new(CreateAuthenticationSessionStatus.Disabled, null, null);

    public static CreateAuthenticationSessionResult InvalidToken() =>
        new(CreateAuthenticationSessionStatus.InvalidToken, null, null);

    public static CreateAuthenticationSessionResult IdentityNotAllowed() =>
        new(CreateAuthenticationSessionStatus.IdentityNotAllowed, null, null);

    public static CreateAuthenticationSessionResult Created(
        AuthenticationIdentityDto identity,
        IssuedSessionToken session) =>
        new(CreateAuthenticationSessionStatus.Created, identity, session);
}
