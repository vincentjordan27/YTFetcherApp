namespace YtFetcher.Application.Authentication.DTO.Result;

/// <summary>
/// Opaque internal application session token produced for an already-authorized application user, together
/// with the instant it stops being valid. The token is only ever transported in the backend session cookie.
/// </summary>
public sealed record IssuedSessionToken(string Token, DateTimeOffset ExpiresAtUtc);
