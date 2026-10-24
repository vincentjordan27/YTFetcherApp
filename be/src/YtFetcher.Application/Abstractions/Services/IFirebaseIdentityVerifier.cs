using YtFetcher.Application.Authentication.DTO.Result;

namespace YtFetcher.Application.Abstractions.Services;

/// <summary>
/// Verifies a Firebase ID token through the Firebase Admin SDK and normalizes the result. Implementations own
/// provider validation, so no raw provider payload or unverified caller data crosses this boundary.
/// </summary>
public interface IFirebaseIdentityVerifier
{
    /// <summary>
    /// Returns the verified identity, or <c>null</c> when the token is missing, malformed, expired, signed for
    /// another project, or has no verified email address. A denial is never an exception at this boundary.
    /// </summary>
    Task<FirebaseIdentityResult?> VerifyIdTokenAsync(string idToken, CancellationToken cancellationToken);
}
