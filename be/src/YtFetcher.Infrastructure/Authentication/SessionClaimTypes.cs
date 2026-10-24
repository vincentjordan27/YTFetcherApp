namespace YtFetcher.Infrastructure.Authentication;

/// <summary>
/// Claim types carried by the internal application session token. They are the only identity facts the API
/// reads for authorization; the Firebase access token, provider claims, and refresh material are never included.
/// </summary>
public static class SessionClaimTypes
{
    /// <summary>Application user id. It is the JWT subject.</summary>
    public const string UserId = "sub";

    /// <summary>Firebase UID of the verified Google account behind this application user.</summary>
    public const string FirebaseUid = "firebase_uid";

    /// <summary>Application user email, taken from the verified Firebase identity when the user was created.</summary>
    public const string Email = "email";

    /// <summary>Application role. Only <c>ADMIN</c> exists today.</summary>
    public const string Role = "role";
}
