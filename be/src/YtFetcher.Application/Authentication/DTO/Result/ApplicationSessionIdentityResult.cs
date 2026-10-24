namespace YtFetcher.Application.Authentication.DTO.Result;

/// <summary>
/// Identity facts recovered from a validated internal application session token.
/// </summary>
public sealed record ApplicationSessionIdentityResult(Guid UserId, string Email, string Role);
