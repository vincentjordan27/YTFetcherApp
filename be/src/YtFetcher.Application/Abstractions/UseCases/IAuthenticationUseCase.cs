using YtFetcher.Application.Authentication.DTO.Response;
using YtFetcher.Application.Authentication.DTO.Result;

namespace YtFetcher.Application.Abstractions.UseCases;

/// <summary>
/// Application boundary for the optional authentication feature: bootstrap configuration, session exchange,
/// and current-session lookup. All three belong to the same behavior, so they share one use case instead of
/// one class per method.
/// </summary>
public interface IAuthenticationUseCase
{
    /// <summary>
    /// Reports the current session. A disabled instance reports disabled; an enabled instance reports
    /// unauthenticated when there is no application user id or the stored user may no longer hold a session.
    /// </summary>
    Task<CurrentSessionResponse> GetCurrentSessionAsync(
        Guid? applicationUserId,
        CancellationToken cancellationToken);

    /// <summary>
    /// Exchanges a Firebase ID token for an application session. The caller writes the cookie only for a
    /// created session.
    /// </summary>
    Task<CreateAuthenticationSessionResult> CreateSessionAsync(
        string firebaseIdToken,
        CancellationToken cancellationToken);
}
