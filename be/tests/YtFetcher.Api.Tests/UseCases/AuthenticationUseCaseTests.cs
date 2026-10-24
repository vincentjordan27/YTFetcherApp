using YtFetcher.Application.Abstractions.Repositories;
using YtFetcher.Application.Abstractions.Services;
using YtFetcher.Application.Authentication.DTO.Result;
using YtFetcher.Application.Authentication.UseCases;
using YtFetcher.Core.Users;

namespace YtFetcher.Api.Tests.UseCases;

/// <summary>
/// Authentication orchestration: the mode gate, the Firebase denial path, user persistence delegation, and
/// current-session lookup. Every collaborator is a fake, so no Firebase project, real token, or database is
/// involved.
/// </summary>
public class AuthenticationUseCaseTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 9, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task A_disabled_instance_reports_disabled_without_any_authentication_dependencies()
    {
        var useCase = new DisabledAuthenticationUseCase();

        var result = await useCase.CreateSessionAsync("any-token", CancellationToken.None);

        Assert.Equal(CreateAuthenticationSessionStatus.Disabled, result.Status);
        Assert.Null(result.Session);
        Assert.Null(result.Identity);
        var current = await useCase.GetCurrentSessionAsync(Guid.NewGuid(), CancellationToken.None);

        Assert.False(current.AuthenticationEnabled);
        Assert.False(current.Authenticated);
    }

    [Fact]
    public async Task An_unverifiable_token_is_denied_without_touching_persistence()
    {
        var repository = new RecordingUserRepository();
        var useCase = Create(new RecordingVerifier { Identity = null }, repository, out _);

        var result = await useCase.CreateSessionAsync("unverifiable", CancellationToken.None);

        Assert.Equal(CreateAuthenticationSessionStatus.InvalidToken, result.Status);
        Assert.Null(result.Session);
        Assert.Equal(0, repository.RegisterCallCount);
    }

    [Fact]
    public async Task A_denied_identity_issues_no_session()
    {
        var verifier = new RecordingVerifier { Identity = new FirebaseIdentityResult("uid", "blocked@example.test", null) };
        var repository = new RecordingUserRepository { RegistrationResult = null };
        var useCase = Create(verifier, repository, out var issuer);

        var result = await useCase.CreateSessionAsync("verified", CancellationToken.None);

        Assert.Equal(CreateAuthenticationSessionStatus.IdentityNotAllowed, result.Status);
        Assert.Null(result.Session);
        Assert.Equal(1, repository.RegisterCallCount);
        Assert.Equal(0, issuer.CallCount);
    }

    [Fact]
    public async Task A_created_session_carries_the_persisted_identity_and_the_issued_token()
    {
        var identity = new FirebaseIdentityResult("uid", "person@example.test", "Person One");
        var verifier = new RecordingVerifier { Identity = identity };
        var user = User.CreateFirstLogin("uid", "person@example.test", "Person One", Now);
        var repository = new RecordingUserRepository { RegistrationResult = user };
        var useCase = Create(verifier, repository, out var issuer);

        var result = await useCase.CreateSessionAsync("verified", CancellationToken.None);

        Assert.Equal(CreateAuthenticationSessionStatus.Created, result.Status);
        Assert.Equal(user.Id, result.Identity!.UserId);
        Assert.Equal("person@example.test", result.Identity.Email);
        Assert.Equal(User.AdminRole, result.Identity.Role);
        Assert.Equal("issued-token", result.Session!.Token);
        Assert.Equal(Now.AddMinutes(30), result.Session.ExpiresAtUtc);
        Assert.Equal(identity, repository.LastRegisteredIdentity);
        Assert.Equal(Now, repository.LastRegisteredAt);
        Assert.Equal(1, issuer.CallCount);
    }

    [Fact]
    public async Task Session_lookup_reports_unauthenticated_without_a_user_or_for_a_denied_user()
    {
        var useCase = Create(new RecordingVerifier(), new RecordingUserRepository(), out _);

        var missing = await useCase.GetCurrentSessionAsync(null, CancellationToken.None);
        Assert.True(missing.AuthenticationEnabled);
        Assert.False(missing.Authenticated);
        Assert.Null(missing.Identity);

        var unknown = await useCase.GetCurrentSessionAsync(Guid.NewGuid(), CancellationToken.None);
        Assert.False(unknown.Authenticated);

        var deleted = User.CreateFirstLogin("uid", "gone@example.test", null, Now);
        deleted.IsDeleted = true;
        var withDeleted = Create(
            new RecordingVerifier(),
            new RecordingUserRepository { LookupResult = deleted },
            out _);

        var denied = await withDeleted.GetCurrentSessionAsync(deleted.Id, CancellationToken.None);

        Assert.True(denied.AuthenticationEnabled);
        Assert.False(denied.Authenticated);
        Assert.Null(denied.Identity);
    }

    [Fact]
    public async Task Session_lookup_reports_an_allowed_identity()
    {
        var user = User.CreateFirstLogin("uid", "person@example.test", "Person One", Now);
        var useCase = Create(
            new RecordingVerifier(),
            new RecordingUserRepository { LookupResult = user },
            out _);

        var current = await useCase.GetCurrentSessionAsync(user.Id, CancellationToken.None);

        Assert.True(current.Authenticated);
        Assert.Equal(user.Id, current.Identity!.UserId);
        Assert.Equal("person@example.test", current.Identity.Email);
        Assert.Equal("Person One", current.Identity.DisplayName);
    }

    private static AuthenticationUseCase Create(
        RecordingVerifier verifier,
        RecordingUserRepository repository,
        out RecordingSessionTokenIssuer issuer)
    {
        issuer = new RecordingSessionTokenIssuer(new IssuedSessionToken("issued-token", Now.AddMinutes(30)));

        return new AuthenticationUseCase(
            verifier,
            issuer,
            repository,
            new FixedTimeProvider(Now));
    }

    private sealed class FixedTimeProvider(DateTimeOffset utcNow) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => utcNow;
    }

    private sealed class RecordingVerifier : IFirebaseIdentityVerifier
    {
        public FirebaseIdentityResult? Identity { get; init; }

        public int CallCount { get; private set; }

        public Task<FirebaseIdentityResult?> VerifyIdTokenAsync(string idToken, CancellationToken cancellationToken)
        {
            CallCount += 1;

            return Task.FromResult(Identity);
        }
    }

    private sealed class RecordingSessionTokenIssuer(IssuedSessionToken token) : IApplicationSessionTokenIssuer
    {
        public int CallCount { get; private set; }

        public IssuedSessionToken Issue(User user)
        {
            CallCount += 1;

            return token;
        }
    }

    private sealed class RecordingUserRepository : IUserRepository
    {
        public User? LookupResult { get; init; }

        public User? RegistrationResult { get; init; }

        public int RegisterCallCount { get; private set; }

        public FirebaseIdentityResult? LastRegisteredIdentity { get; private set; }

        public DateTimeOffset? LastRegisteredAt { get; private set; }

        public Task<User?> FindByIdAsync(Guid userId, CancellationToken cancellationToken) =>
            Task.FromResult(LookupResult);

        public Task<User?> RegisterLoginAsync(
            FirebaseIdentityResult identity,
            DateTimeOffset utcNow,
            CancellationToken cancellationToken)
        {
            RegisterCallCount += 1;
            LastRegisteredIdentity = identity;
            LastRegisteredAt = utcNow;

            return Task.FromResult(RegistrationResult);
        }
    }
}
