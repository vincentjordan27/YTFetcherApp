namespace YtFetcher.Application.Authentication.DTO.Request;

/// <summary>
/// Body of the logout action. The action carries no input, but every non-GET action still uses a dedicated
/// request type so the transport contract stays explicit and a future field cannot silently ride along.
/// </summary>
public sealed record LogoutRequest();
