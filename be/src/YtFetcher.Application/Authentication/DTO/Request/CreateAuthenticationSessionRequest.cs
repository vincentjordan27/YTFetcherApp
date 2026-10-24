namespace YtFetcher.Application.Authentication.DTO.Request;

/// <summary>
/// Body of the session-exchange action: the Firebase ID token the browser obtained from the Google sign-in
/// popup. The browser never submits an email, Firebase UID, role, or display name.
/// </summary>
public sealed record CreateAuthenticationSessionRequest(string FirebaseIdToken);
