using YtFetcher.Application.Authentication.DTO.Result;

namespace YtFetcher.Application.Abstractions.Services;

/// <summary>
/// Validates an internal application session token without exposing the JWT implementation to the API layer.
/// </summary>
public interface IApplicationSessionTokenValidator
{
    Task<ApplicationSessionIdentityResult?> ValidateAsync(string token, CancellationToken cancellationToken);
}
