namespace YtFetcher.Application.Abstractions.Services;

/// <summary>
/// Authentication runtime configuration, resolved once at startup because switching the mode requires a
/// restart. It exposes the enabled flag and the frontend origins the API may accept; it never exposes Firebase
/// Admin credentials, browser configuration, or the session signing key.
/// </summary>
public interface IAuthenticationRuntimeConfiguration
{
    bool AuthenticationEnabled { get; }

    /// <summary>
    /// Exact frontend origins allowed to call this API with credentials. It is empty when no origin is
    /// configured, which rejects every cross-origin request instead of falling back to a wildcard.
    /// </summary>
    IReadOnlyList<string> AllowedFrontendOrigins { get; }
}
