namespace YtFetcher.Application.Authentication.DTO.Result;

/// <summary>
/// Normalized, already-verified Firebase identity. It is produced by the Firebase verification port and is
/// the only Firebase-derived data the authentication use case may trust: no SDK type, claim principal, or raw
/// provider payload crosses that boundary.
/// </summary>
public sealed record FirebaseIdentityResult(
    string FirebaseUid,
    string VerifiedEmail,
    string? DisplayName);
