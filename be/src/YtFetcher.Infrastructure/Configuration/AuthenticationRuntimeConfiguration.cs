using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;
using YtFetcher.Application.Abstractions.Services;

namespace YtFetcher.Infrastructure.Configuration;

/// <summary>
/// Authentication runtime configuration. It owns every Firebase and JWT setting, so the enabled flag, the
/// allowed frontend origins are the only things an outer layer can see.
/// The mode is fixed for the lifetime of the host: switching it requires an API restart.
/// </summary>
public sealed class AuthenticationRuntimeConfiguration : IAuthenticationRuntimeConfiguration
{
    private const string AuthEnabledKey = "Auth:Enabled";
    private const string FirebaseProjectIdKey = "Auth:Firebase:ProjectId";
    private const string FirebaseServiceAccountFilePathKey = "Auth:Firebase:ServiceAccountFilePath";
    private const string JwtIssuerKey = "Auth:Jwt:Issuer";
    private const string JwtAudienceKey = "Auth:Jwt:Audience";
    private const string JwtSigningKeyKey = "Auth:Jwt:SigningKey";
    private const string JwtSessionLifetimeMinutesKey = "Auth:Jwt:SessionLifetimeMinutes";
    private const string FrontendOriginKey = "Auth:FrontendOrigin";

    /// <summary>Development Vite origins for both local HTTP and HTTPS profiles.</summary>
    internal const string DevelopmentFrontendOrigin = "http://localhost:5173";
    internal const string DevelopmentLoopbackFrontendOrigin = "http://127.0.0.1:5173";
    internal const string DevelopmentSecureFrontendOrigin = "https://localhost:5173";
    internal const string DevelopmentSecureLoopbackFrontendOrigin = "https://127.0.0.1:5173";

    /// <summary>Shortest accepted session signing secret; HMAC-SHA256 needs at least 256 bits of key material.</summary>
    public const int MinimumSigningKeyLength = 32;

    /// <summary>Session lifetime used when the deployment does not configure one, in minutes.</summary>
    public const int DefaultSessionLifetimeMinutes = 720;

    private const int MaximumSessionLifetimeMinutes = 60 * 24 * 30;

    private AuthenticationRuntimeConfiguration(
        bool authenticationEnabled,
        string? firebaseProjectId,
        string? firebaseServiceAccountFilePath,
        string? jwtIssuer,
        string? jwtAudience,
        string? jwtSigningKey,
        int sessionLifetimeMinutes,
        IReadOnlyList<string> allowedFrontendOrigins)
    {
        AuthenticationEnabled = authenticationEnabled;
        FirebaseProjectId = firebaseProjectId;
        FirebaseServiceAccountFilePath = firebaseServiceAccountFilePath;
        JwtIssuer = jwtIssuer;
        JwtAudience = jwtAudience;
        JwtSigningKey = jwtSigningKey;
        SessionLifetimeMinutes = sessionLifetimeMinutes;
        AllowedFrontendOrigins = allowedFrontendOrigins;
    }

    public bool AuthenticationEnabled { get; }

    /// <summary>Firebase project whose ID tokens are accepted. Server-only configuration.</summary>
    internal string? FirebaseProjectId { get; }

    /// <summary>Absolute container path to the mounted Firebase Admin SDK service-account credential.</summary>
    internal string? FirebaseServiceAccountFilePath { get; }

    internal string? JwtIssuer { get; }

    internal string? JwtAudience { get; }

    internal string? JwtSigningKey { get; }

    internal int SessionLifetimeMinutes { get; }

    public IReadOnlyList<string> AllowedFrontendOrigins { get; }

    /// <summary>
    /// Reads and validates the authentication mode. An enabled instance must have its Firebase, JWT, and
    /// frontend-origin configuration resolved here, so a misconfigured deployment stops at startup with a
    /// readable error instead of silently running anonymously or failing on the first login.
    /// </summary>
    public static AuthenticationRuntimeConfiguration Load(
        IConfiguration configuration,
        IHostEnvironment environment)
    {
        var enabled = ReadEnabled(configuration, environment);

        if (!enabled)
        {
            // Disabled is a supported deployment: no Firebase section, JWT section, or secret is required.
            return new AuthenticationRuntimeConfiguration(
                false,
                null,
                null,
                null,
                null,
                null,
                DefaultSessionLifetimeMinutes,
                ReadFrontendOrigins(configuration, environment));
        }

        var projectId = ReadRequired(configuration, FirebaseProjectIdKey);
        var serviceAccountFilePath = ReadRequired(configuration, FirebaseServiceAccountFilePathKey);
        var signingKey = ReadRequired(configuration, JwtSigningKeyKey);
        if (signingKey.Length < MinimumSigningKeyLength)
        {
            throw new InvalidOperationException(
                $"Configuration value '{JwtSigningKeyKey}' must be at least {MinimumSigningKeyLength} characters.");
        }

        return new AuthenticationRuntimeConfiguration(
            true,
            projectId,
            serviceAccountFilePath,
            ReadRequired(configuration, JwtIssuerKey),
            ReadRequired(configuration, JwtAudienceKey),
            signingKey,
            ReadSessionLifetimeMinutes(configuration),
            ReadFrontendOrigins(configuration, environment));
    }

    private static bool ReadEnabled(IConfiguration configuration, IHostEnvironment environment)
    {
        var raw = configuration[AuthEnabledKey];

        if (string.IsNullOrWhiteSpace(raw))
        {
            // Only Development may omit the key. Every other environment, including Production, must state the
            // mode explicitly so a deployment can never fall back to anonymous access by accident.
            if (environment.IsDevelopment())
            {
                return false;
            }

            throw new InvalidOperationException(
                $"Configuration value '{AuthEnabledKey}' is required outside Development. Set it to 'true' or 'false'.");
        }

        return bool.TryParse(raw.Trim(), out var parsed)
            ? parsed
            : throw new InvalidOperationException(
                $"Configuration value '{AuthEnabledKey}' must be 'true' or 'false'.");
    }

    private static IReadOnlyList<string> ReadFrontendOrigins(
        IConfiguration configuration,
        IHostEnvironment environment)
    {
        if (environment.IsDevelopment())
        {
            // Vite may run with either profile locally. Both origins are explicit because this API carries
            // credentials and must never use a wildcard CORS policy.
            return
            [
                DevelopmentFrontendOrigin,
                DevelopmentLoopbackFrontendOrigin,
                DevelopmentSecureFrontendOrigin,
                DevelopmentSecureLoopbackFrontendOrigin
            ];
        }

        var configured = ReadOptional(configuration, FrontendOriginKey)
            ?? throw new InvalidOperationException(
                $"Configuration value '{FrontendOriginKey}' is required outside Development.");

        return [NormalizeOrigin(configured)];
    }

    private static string NormalizeOrigin(string origin)
    {
        var trimmed = origin.Trim().TrimEnd('/');

        if (!Uri.TryCreate(trimmed, UriKind.Absolute, out var uri) ||
            (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps))
        {
            throw new InvalidOperationException(
                $"Configuration value '{FrontendOriginKey}' must be an absolute http or https origin.");
        }

        return trimmed;
    }

    private static int ReadSessionLifetimeMinutes(IConfiguration configuration)
    {
        var raw = ReadOptional(configuration, JwtSessionLifetimeMinutesKey);
        if (raw is null)
        {
            return DefaultSessionLifetimeMinutes;
        }

        if (!int.TryParse(raw, System.Globalization.NumberStyles.Integer, System.Globalization.CultureInfo.InvariantCulture, out var minutes) ||
            minutes < 1 ||
            minutes > MaximumSessionLifetimeMinutes)
        {
            throw new InvalidOperationException(
                $"Configuration value '{JwtSessionLifetimeMinutesKey}' must be a whole number of minutes between 1 and {MaximumSessionLifetimeMinutes}.");
        }

        return minutes;
    }

    private static string ReadRequired(IConfiguration configuration, string key) =>
        ReadOptional(configuration, key)
        ?? throw new InvalidOperationException(
            $"Configuration value '{key}' is required when '{AuthEnabledKey}' is 'true'.");

    private static string? ReadOptional(IConfiguration configuration, string key) =>
        configuration[key] is { Length: > 0 } value ? value.Trim() : null;
}
