using YtFetcher.Application.Abstractions.Services;
using YtFetcher.Application.Authentication.DTO.Result;

namespace YtFetcher.Api.Tests.Authentication;

/// <summary>
/// Scripted Firebase verification stand-in. Every auth-enabled host test uses it, so no test needs Firebase
/// credentials, a Firebase project, or a network call.
/// </summary>
internal sealed class FakeFirebaseIdentityVerifier : IFirebaseIdentityVerifier
{
    private readonly Dictionary<string, FirebaseIdentityResult> identities = new(StringComparer.Ordinal);

    internal int VerificationCount { get; private set; }

    internal void Accept(string idToken, string firebaseUid, string email, string? displayName = null) =>
        identities[idToken] = new FirebaseIdentityResult(firebaseUid, email, displayName);

    public Task<FirebaseIdentityResult?> VerifyIdTokenAsync(
        string idToken,
        CancellationToken cancellationToken)
    {
        VerificationCount += 1;

        return Task.FromResult(identities.TryGetValue(idToken, out var identity) ? identity : null);
    }
}
