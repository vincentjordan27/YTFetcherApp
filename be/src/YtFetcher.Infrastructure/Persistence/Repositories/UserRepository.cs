using Microsoft.EntityFrameworkCore;
using Npgsql;
using YtFetcher.Application.Abstractions.Repositories;
using YtFetcher.Application.Authentication.DTO.Result;
using YtFetcher.Core.Users;
using YtFetcher.Infrastructure.Persistence;

namespace YtFetcher.Infrastructure.Persistence.Repositories;

public sealed class UserRepository(YtFetcherDbContext dbContext) : IUserRepository
{
    public async Task<User?> FindByIdAsync(Guid userId, CancellationToken cancellationToken) =>
        await dbContext.Users
            .AsNoTracking()
            // Soft-deleted rows are intentionally returned: an existing session for a deleted user must be
            // reported as unauthenticated instead of looking like a user that never existed.
            .SingleOrDefaultAsync(user => user.Id == userId, cancellationToken);

    public async Task<User?> RegisterLoginAsync(
        FirebaseIdentityResult identity,
        DateTimeOffset utcNow,
        CancellationToken cancellationToken)
    {
        var existing = await FindTrackedByFirebaseUidAsync(identity.FirebaseUid, cancellationToken);
        if (existing is not null)
        {
            return await RecordLoginAsync(existing, utcNow, cancellationToken);
        }

        var user = User.CreateFirstLogin(
            identity.FirebaseUid,
            identity.VerifiedEmail,
            identity.DisplayName,
            utcNow);

        dbContext.Users.Add(user);

        try
        {
            // One ordinary single-table save: no transaction is created merely to insert one user row.
            await dbContext.SaveChangesAsync(cancellationToken);
            return user;
        }
        catch (DbUpdateException exception) when (
            exception.InnerException is PostgresException { SqlState: PostgresErrorCodes.UniqueViolation })
        {
            // The unique Firebase UID or the case-insensitive email index rejected this insert, which means a
            // concurrent first login, or a different application identity, already owns the row. The durable
            // uniqueness constraints are the source of truth, so converge on the stored row instead of
            // creating a second one.
            dbContext.Entry(user).State = EntityState.Detached;

            var converged = await FindTrackedByFirebaseUidAsync(identity.FirebaseUid, cancellationToken);

            return converged is null
                ? null
                : await RecordLoginAsync(converged, utcNow, cancellationToken);
        }
    }

    private async Task<User?> FindTrackedByFirebaseUidAsync(string firebaseUid, CancellationToken cancellationToken) =>
        await dbContext.Users.SingleOrDefaultAsync(
            user => user.FirebaseUid == firebaseUid,
            cancellationToken);

    private async Task<User?> RecordLoginAsync(User user, DateTimeOffset utcNow, CancellationToken cancellationToken)
    {
        if (!user.IsAllowed)
        {
            // A deactivated or soft-deleted row denies the login; its state is never revived by signing in.
            return null;
        }

        user.RecordLogin(utcNow);
        await dbContext.SaveChangesAsync(cancellationToken);

        return user;
    }
}
