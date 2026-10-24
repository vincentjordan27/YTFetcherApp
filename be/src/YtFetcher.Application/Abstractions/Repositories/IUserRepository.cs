using YtFetcher.Application.Authentication.DTO.Result;
using YtFetcher.Core.Users;

namespace YtFetcher.Application.Abstractions.Repositories;

/// <summary>
/// Persistence boundary for the application user aggregate.
/// </summary>
public interface IUserRepository
{
    /// <summary>
    /// Reads a user by application id for the current-session lookup. Soft-deleted rows are returned as well,
    /// because an existing session for a deleted user must be reported as unauthenticated rather than
    /// silently treated as a missing user. Returns <c>null</c> when no such row exists.
    /// </summary>
    Task<User?> FindByIdAsync(Guid userId, CancellationToken cancellationToken);

    /// <summary>
    /// Persists one verified login: it creates the application user when this Firebase identity logs in for
    /// the first time, otherwise it records the login on the existing row. The unique Firebase UID and the
    /// case-insensitive email index remain the durable invariant, so a concurrent first login converges on
    /// the stored row instead of creating a second one. Returns <c>null</c> when the identity must be denied:
    /// the stored row is deactivated or soft-deleted, or the verified email already belongs to another
    /// application identity.
    /// </summary>
    Task<User?> RegisterLoginAsync(
        FirebaseIdentityResult identity,
        DateTimeOffset utcNow,
        CancellationToken cancellationToken);
}
