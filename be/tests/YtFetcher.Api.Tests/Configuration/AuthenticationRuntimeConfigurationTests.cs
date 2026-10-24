using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;
using YtFetcher.Infrastructure.Configuration;

namespace YtFetcher.Api.Tests.Configuration;

/// <summary>
/// The authentication mode is a deployment choice, so its configuration contract is a startup contract:
/// Production must state it explicitly, Development may omit it, and an enabled instance must fail fast when
/// its Firebase or JWT configuration is missing or unusable.
/// </summary>
public class AuthenticationRuntimeConfigurationTests
{
    private const string ValidSigningKey = "unit-test-session-signing-key-0123456789";

    [Fact]
    public void Production_without_the_enabled_flag_stops_startup()
    {
        var configuration = CreateConfiguration();

        var exception = Assert.Throws<InvalidOperationException>(
            () => AuthenticationRuntimeConfiguration.Load(configuration, new TestHostEnvironment(Environments.Production)));

        Assert.Contains("Auth:Enabled", exception.Message, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("yes")]
    [InlineData("1")]
    [InlineData("enabled")]
    public void An_unparseable_enabled_flag_stops_startup(string value)
    {
        var configuration = CreateConfiguration(("Auth:Enabled", value));

        var exception = Assert.Throws<InvalidOperationException>(
            () => AuthenticationRuntimeConfiguration.Load(configuration, new TestHostEnvironment(Environments.Development)));

        Assert.Contains("Auth:Enabled", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Development_without_the_enabled_flag_resolves_to_disabled()
    {
        var configuration = AuthenticationRuntimeConfiguration.Load(
            CreateConfiguration(),
            new TestHostEnvironment(Environments.Development));

        Assert.False(configuration.AuthenticationEnabled);
        Assert.Contains("http://localhost:5173", configuration.AllowedFrontendOrigins);
    }

    [Fact]
    public void Production_disabled_mode_needs_no_firebase_or_jwt_configuration_but_requires_a_frontend_origin()
    {
        // Firebase and JWT values remain optional while disabled, but a browser deployment still needs an
        // explicit allowed origin. Otherwise the frontend is silently blocked by credentialed CORS.
        var exception = Assert.Throws<InvalidOperationException>(
            () => AuthenticationRuntimeConfiguration.Load(
                CreateConfiguration(("Auth:Enabled", "false")),
                new TestHostEnvironment(Environments.Production)));

        Assert.Contains("Auth:FrontendOrigin", exception.Message, StringComparison.Ordinal);

        var configuration = AuthenticationRuntimeConfiguration.Load(
            CreateConfiguration(
                ("Auth:Enabled", "false"),
                ("Auth:FrontendOrigin", "https://app.example.test")),
            new TestHostEnvironment(Environments.Production));

        Assert.False(configuration.AuthenticationEnabled);
        Assert.Equal(["https://app.example.test"], configuration.AllowedFrontendOrigins);
    }

    [Theory]
    [InlineData("Auth:Jwt:Issuer")]
    [InlineData("Auth:Jwt:Audience")]
    [InlineData("Auth:Jwt:SigningKey")]
    [InlineData("Auth:Firebase:ProjectId")]
    [InlineData("Auth:Firebase:ServiceAccountFilePath")]
    public void Enabled_mode_reports_every_missing_required_value(string missingKey)
    {
        var values = EnabledValues()
            .Where(pair => !string.Equals(pair.Key, missingKey, StringComparison.Ordinal))
            .ToArray();

        var exception = Assert.Throws<InvalidOperationException>(
            () => AuthenticationRuntimeConfiguration.Load(
                CreateConfiguration(values),
                new TestHostEnvironment(Environments.Development)));

        Assert.Contains(missingKey, exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void An_enabled_short_signing_key_stops_startup()
    {
        var values = EnabledValues()
            .Where(pair => !string.Equals(pair.Key, "Auth:Jwt:SigningKey", StringComparison.Ordinal))
            .Append(("Auth:Jwt:SigningKey", "too-short"))
            .ToArray();
        var configuration = CreateConfiguration(values);

        var exception = Assert.Throws<InvalidOperationException>(
            () => AuthenticationRuntimeConfiguration.Load(configuration, new TestHostEnvironment(Environments.Development)));

        Assert.Contains("Auth:Jwt:SigningKey", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Enabled_configuration_requires_only_server_side_firebase_project_identity()
    {
        var configuration = AuthenticationRuntimeConfiguration.Load(
            CreateConfiguration(EnabledValues()),
            new TestHostEnvironment(Environments.Development));

        Assert.True(configuration.AuthenticationEnabled);

        // Local development keeps explicit HTTP and HTTPS Vite origins and never a wildcard.
        Assert.Equal(4, configuration.AllowedFrontendOrigins.Count);
    }

    [Fact]
    public void Production_enabled_requires_the_single_frontend_origin()
    {
        var exception = Assert.Throws<InvalidOperationException>(
            () => AuthenticationRuntimeConfiguration.Load(
                CreateConfiguration(EnabledValues()),
                new TestHostEnvironment(Environments.Production)));

        Assert.Contains("Auth:FrontendOrigin", exception.Message, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("not-an-origin")]
    [InlineData("ftp://example.test")]
    public void Production_enabled_rejects_a_non_http_origin(string origin)
    {
        var configuration = CreateConfiguration(
            EnabledValues().Append(("Auth:FrontendOrigin", origin)).ToArray());

        var exception = Assert.Throws<InvalidOperationException>(
            () => AuthenticationRuntimeConfiguration.Load(configuration, new TestHostEnvironment(Environments.Production)));

        Assert.Contains("Auth:FrontendOrigin", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Production_enabled_reads_its_one_frontend_origin()
    {
        var configuration = AuthenticationRuntimeConfiguration.Load(
            CreateConfiguration(EnabledValues().Append(("Auth:FrontendOrigin", "https://app.example.test/")).ToArray()),
            new TestHostEnvironment(Environments.Production));

        Assert.Equal(["https://app.example.test"], configuration.AllowedFrontendOrigins);
    }

    private static (string Key, string? Value)[] EnabledValues() =>
    [
        ("Auth:Enabled", "true"),
        ("Auth:Jwt:Issuer", "yt-fetcher"),
        ("Auth:Jwt:Audience", "yt-fetcher-web"),
        ("Auth:Jwt:SigningKey", ValidSigningKey),
        ("Auth:Firebase:ProjectId", "test-project"),
        ("Auth:Firebase:ServiceAccountFilePath", "/run/secrets/firebase-admin-sdk.json")
    ];

    private static IConfiguration CreateConfiguration(params (string Key, string? Value)[] overrides) =>
        new ConfigurationBuilder()
            .AddInMemoryCollection(overrides.Select(pair => new KeyValuePair<string, string?>(pair.Key, pair.Value)))
            .Build();
}
