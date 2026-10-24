using YtFetcher.Application.Abstractions.Repositories;
using YtFetcher.Application.Abstractions.Services;
using YtFetcher.Application.Abstractions.UseCases;
using YtFetcher.Application.Authentication.DTO.Response;
using YtFetcher.Application.Authentication.DTO.Result;

namespace YtFetcher.Application.Authentication.UseCases;

/// <summary>
/// The optional-authentication behavior. The mode is a deployment choice resolved at startup, so a disabled
/// instance performs no Firebase verification, no user persistence, and no token issuance, while an enabled
/// instance derives every identity fact from the verified Firebase token and the stored application user.
/// </summary>
public sealed class AuthenticationUseCase(
    IFirebaseIdentityVerifier firebaseIdentityVerifier,
    IApplicationSessionTokenIssuer sessionTokenIssuer,
    IUserRepository userRepository,
    TimeProvider timeProvider) : IAuthenticationUseCase
{
    public async Task<CurrentSessionResponse> GetCurrentSessionAsync(
        Guid? applicationUserId,
        CancellationToken cancellationToken)
    {
        if (applicationUserId is null)
        {
            return new CurrentSessionResponse(true, false, null);
        }

        var user = await userRepository.FindByIdAsync(applicationUserId.Value, cancellationToken);

        // A cookie can outlive the access it was issued for: a deactivated or soft-deleted user must report
        // an unauthenticated session instead of a usable identity.
        return user is null || !user.IsAllowed
            ? new CurrentSessionResponse(true, false, null)
            : new CurrentSessionResponse(true, true, AuthenticationIdentityDto.ToIdentity(user));
    }

    public async Task<CreateAuthenticationSessionResult> CreateSessionAsync(
        string firebaseIdToken,
        CancellationToken cancellationToken)
    {
        var identity = await firebaseIdentityVerifier.VerifyIdTokenAsync(firebaseIdToken, cancellationToken);
        if (identity is null)
        {
            return CreateAuthenticationSessionResult.InvalidToken();
        }

        var utcNow = timeProvider.GetUtcNow();
        var user = await userRepository.RegisterLoginAsync(identity, utcNow, cancellationToken);
        if (user is null)
        {
            return CreateAuthenticationSessionResult.IdentityNotAllowed();
        }

        // The session is issued only after the user row is durably stored, so an issued cookie always
        // refers to a persisted application identity.
        return CreateAuthenticationSessionResult.Created(
            AuthenticationIdentityDto.ToIdentity(user),
            sessionTokenIssuer.Issue(user));
    }
}
