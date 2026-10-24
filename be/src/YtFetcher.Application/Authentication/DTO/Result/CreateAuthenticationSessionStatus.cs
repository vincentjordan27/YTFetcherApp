namespace YtFetcher.Application.Authentication.DTO.Result;

/// <summary>Outcome of a session-exchange attempt. Only <see cref="Created"/> issues a session cookie.</summary>
public enum CreateAuthenticationSessionStatus
{
    /// <summary>The verified identity was accepted and an application session was issued.</summary>
    Created,

    /// <summary>This instance runs without authentication, so no Firebase work was performed.</summary>
    Disabled,

    /// <summary>The submitted Firebase ID token was missing, malformed, expired, or had no verified email.</summary>
    InvalidToken,

    /// <summary>The verified identity is deactivated, soft-deleted, or already owned by another application identity.</summary>
    IdentityNotAllowed
}
