namespace YtFetcher.Api.Tests.Authentication;

/// <summary>
/// Temporarily supplies authentication configuration that a runtime configuration file would normally provide.
/// It is applied after <see cref="HostConfigurationScope"/>, which supplies the anonymous default, so
/// declaration order decides the mode and disposal order restores it.
/// </summary>
internal sealed class AuthenticationConfigurationScope : IDisposable
{
    /// <summary>Signing secret shared with tests that must craft their own token.</summary>
    internal const string SigningKey = "integration-test-session-signing-key-0123456789";

    internal const string Issuer = "yt-fetcher-tests";

    internal const string Audience = "yt-fetcher-tests-web";

    internal const string FrontendOrigin = "http://localhost:5173";

    private readonly Dictionary<string, string?> previousValues = [];

    internal AuthenticationConfigurationScope(bool enabled = true, string frontendOrigin = FrontendOrigin)
    {
        var values = new Dictionary<string, string>
        {
            ["Auth__Enabled"] = enabled ? "true" : "false",
            ["Auth__FrontendOrigin"] = frontendOrigin,
            // The Firebase and JWT sections stay present in disabled mode as well: a disabled instance must
            // simply ignore them instead of requiring their absence.
            ["Auth__Jwt__Issuer"] = Issuer,
            ["Auth__Jwt__Audience"] = Audience,
            ["Auth__Jwt__SigningKey"] = SigningKey,
            ["Auth__Firebase__ProjectId"] = "test-project",
            ["Auth__Firebase__ServiceAccountFilePath"] = "/run/secrets/firebase-admin-sdk.json"
        };

        foreach (var (key, value) in values)
        {
            previousValues[key] = Environment.GetEnvironmentVariable(key);
            Environment.SetEnvironmentVariable(key, value);
        }
    }

    public void Dispose()
    {
        foreach (var (key, value) in previousValues)
        {
            Environment.SetEnvironmentVariable(key, value);
        }
    }
}
