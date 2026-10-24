namespace YtFetcher.Application.Authentication.DTO.Response;

/// <summary>
/// Current session state. It is never a token carrier: the session itself only travels in the HttpOnly
/// cookie, so a disabled instance reports <c>AuthenticationEnabled = false</c> and an enabled instance
/// without a valid session simply reports <c>Authenticated = false</c>.
/// </summary>
public sealed record CurrentSessionResponse(
    bool AuthenticationEnabled,
    bool Authenticated,
    AuthenticationIdentityDto? Identity);
