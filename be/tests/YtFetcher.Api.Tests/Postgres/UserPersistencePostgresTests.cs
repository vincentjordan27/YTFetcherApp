using Microsoft.EntityFrameworkCore;
using YtFetcher.Application.Authentication.DTO.Result;
using YtFetcher.Core.Users;
using YtFetcher.Infrastructure.Persistence;
using YtFetcher.Infrastructure.Persistence.Repositories;

namespace YtFetcher.Api.Tests.Postgres;

/// <summary>
/// PostgreSQL-only proof for user registration. The email and Firebase UID uniqueness constraints are the
/// durable invariant behind first-login convergence, and only a real PostgreSQL target enforces them, so this
/// proof is skipped unless the runner supplies <c>YTF_TEST_POSTGRES</c>.
/// </summary>
[Collection(PostgresProofCollection.Name)]
public class UserPersistencePostgresTests : IAsyncLifetime
{
    private static readonly DateTimeOffset Now = new(2026, 10, 9, 12, 0, 0, TimeSpan.Zero);

    private readonly string connectionString = PostgresProofTarget.ConnectionString ?? string.Empty;

    private YtFetcherDbContext dbContext = null!;

    public async Task InitializeAsync()
    {
        if (!PostgresProofTarget.IsAvailable)
        {
            return;
        }

        dbContext = CreateContext();
        await PostgresProofDatabase.RecreateBaselineAsync(dbContext);
    }

    public async Task DisposeAsync()
    {
        if (!PostgresProofTarget.IsAvailable)
        {
            return;
        }

        await PostgresProofDatabase.ResetAsync(dbContext);
        await dbContext.DisposeAsync();
    }

    [PostgresProofFact]
    public async Task Concurrent_first_logins_converge_on_one_user()
    {
        var identity = new FirebaseIdentityResult("firebase-uid-1", "person@example.test", "Person One");

        await using var firstContext = CreateContext();
        await using var secondContext = CreateContext();

        // Two independent scopes, exactly like two concurrent HTTP requests.
        var results = await Task.WhenAll(
            new UserRepository(firstContext).RegisterLoginAsync(identity, Now, CancellationToken.None),
            new UserRepository(secondContext).RegisterLoginAsync(identity, Now, CancellationToken.None));

        Assert.All(results, user => Assert.NotNull(user));
        Assert.Single(results.Select(user => user!.Id).Distinct());

        Assert.Equal(
            1,
            await dbContext.Users.CountAsync(user => user.FirebaseUid == identity.FirebaseUid));
    }

    [PostgresProofFact]
    public async Task A_verified_email_held_by_another_identity_is_denied_without_a_second_row()
    {
        var first = await new UserRepository(dbContext)
            .RegisterLoginAsync(new FirebaseIdentityResult("firebase-uid-1", "shared@example.test", null), Now, CancellationToken.None);

        Assert.NotNull(first);

        await using var otherContext = CreateContext();
        var denied = await new UserRepository(otherContext).RegisterLoginAsync(
            new FirebaseIdentityResult("firebase-uid-2", "SHARED@example.test", null),
            Now,
            CancellationToken.None);

        // The case-insensitive email index is the invariant, so the second identity is denied rather than
        // creating a duplicate row or throwing.
        Assert.Null(denied);
        Assert.Equal(
            1,
            await dbContext.Users.CountAsync(user => user.Email.ToLower() == "shared@example.test"));
    }

    [PostgresProofFact]
    public async Task A_soft_deleted_user_is_denied_and_never_revived()
    {
        var registered = await new UserRepository(dbContext)
            .RegisterLoginAsync(new FirebaseIdentityResult("firebase-uid-3", "gone@example.test", null), Now, CancellationToken.None);

        Assert.NotNull(registered);

        registered!.IsDeleted = true;
        registered.DeletedAt = Now;
        await dbContext.SaveChangesAsync();

        await using var otherContext = CreateContext();
        var denied = await new UserRepository(otherContext).RegisterLoginAsync(
            new FirebaseIdentityResult("firebase-uid-3", "gone@example.test", null),
            Now.AddMinutes(5),
            CancellationToken.None);

        Assert.Null(denied);

        var stored = await dbContext.Users
            .AsNoTracking()
            .SingleAsync(user => user.FirebaseUid == "firebase-uid-3");
        Assert.True(stored.IsDeleted);
    }

    private YtFetcherDbContext CreateContext() =>
        new(new DbContextOptionsBuilder<YtFetcherDbContext>().UseNpgsql(connectionString).Options);
}
