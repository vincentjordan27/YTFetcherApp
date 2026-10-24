using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using Microsoft.Extensions.Hosting;
using YtFetcher.Api.Tests.Authentication;

namespace YtFetcher.Api.Tests.Channels;

/// <summary>
/// CORS is the browser transport boundary: the frontend runs on its own origin and sends the session cookie,
/// so the policy allows exactly the configured origin with credentials. Development keeps the two local Vite
/// origins; every other environment reads its one configured origin, and no policy ever allows a wildcard.
/// </summary>
[Collection(ApiStartupCollection.Name)]
public class CorsPolicyTests
{
    private const string AllowedOrigin = "http://localhost:5173";
    private const string AllowedLoopbackOrigin = "http://127.0.0.1:5173";
    private const string SecureAllowedOrigin = "https://localhost:5173";
    private const string SecureAllowedLoopbackOrigin = "https://127.0.0.1:5173";
    private const string DisallowedOrigin = "http://evil.example";
    private const string ProductionOrigin = "https://app.example.test";

    [Theory]
    [InlineData(AllowedOrigin)]
    [InlineData(AllowedLoopbackOrigin)]
    [InlineData(SecureAllowedOrigin)]
    [InlineData(SecureAllowedLoopbackOrigin)]
    public async Task Preflight_from_an_allowed_vite_origin_is_accepted_with_credentials(string origin)
    {
        using var environment = new EnvironmentConfigurationScope(Environments.Development);
        using var databaseConfiguration = new HostConfigurationScope();
        using var factory = new ChannelApiFactory();
        using var client = factory.CreateClient();

        var response = await SendPreflightAsync(client, origin);

        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
        Assert.Equal(origin, SingleHeader(response, "Access-Control-Allow-Origin"));
        Assert.Equal("true", SingleHeader(response, "Access-Control-Allow-Credentials"));
    }

    [Fact]
    public async Task Simple_request_from_an_allowed_vite_origin_returns_the_allow_origin_header()
    {
        using var environment = new EnvironmentConfigurationScope(Environments.Development);
        using var databaseConfiguration = new HostConfigurationScope();
        using var factory = new ChannelApiFactory();
        using var client = factory.CreateClient();

        using var request = new HttpRequestMessage(HttpMethod.Post, "/api/channels/pending/list")
        {
            Content = JsonContent.Create(new { page = 1, pageSize = 10, searchText = (string?)null })
        };
        request.Headers.Add("Origin", AllowedOrigin);
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));

        var response = await client.SendAsync(request);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(AllowedOrigin, SingleHeader(response, "Access-Control-Allow-Origin"));
    }

    [Fact]
    public async Task Preflight_from_a_disallowed_origin_is_not_granted_cors_access()
    {
        using var environment = new EnvironmentConfigurationScope(Environments.Development);
        using var databaseConfiguration = new HostConfigurationScope();
        using var factory = new ChannelApiFactory();
        using var client = factory.CreateClient();

        var response = await SendPreflightAsync(client, DisallowedOrigin);

        Assert.False(response.Headers.Contains("Access-Control-Allow-Origin"));
    }

    [Fact]
    public async Task Outside_development_only_the_configured_frontend_origin_is_allowed()
    {
        using var environment = new EnvironmentConfigurationScope(Environments.Production);
        using var databaseConfiguration = new HostConfigurationScope();
        using var authenticationConfiguration = new AuthenticationConfigurationScope(
            enabled: false,
            frontendOrigin: ProductionOrigin);
        using var factory = new ChannelApiFactory();
        using var client = factory.CreateClient();

        var allowed = await SendPreflightAsync(client, ProductionOrigin);

        Assert.Equal(HttpStatusCode.NoContent, allowed.StatusCode);
        Assert.Equal(ProductionOrigin, SingleHeader(allowed, "Access-Control-Allow-Origin"));
        Assert.Equal("true", SingleHeader(allowed, "Access-Control-Allow-Credentials"));

        var disallowed = await SendPreflightAsync(client, AllowedOrigin);

        Assert.False(disallowed.Headers.Contains("Access-Control-Allow-Origin"));
    }

    [Fact]
    public void Production_without_a_frontend_origin_does_not_start()
    {
        using var environment = new EnvironmentConfigurationScope(Environments.Production);
        using var databaseConfiguration = new HostConfigurationScope(includeFrontendOrigin: false);
        using var factory = new ChannelApiFactory();

        var exception = Assert.Throws<InvalidOperationException>(() => factory.CreateClient());

        Assert.Contains("Auth:FrontendOrigin", exception.Message, StringComparison.Ordinal);
    }

    private static Task<HttpResponseMessage> SendPreflightAsync(HttpClient client, string origin)
    {
        // The paged list is a body-based POST action, so this is the real preflight the browser sends.
        var request = new HttpRequestMessage(HttpMethod.Options, "/api/channels/pending/list");
        request.Headers.Add("Origin", origin);
        request.Headers.Add("Access-Control-Request-Method", "POST");
        request.Headers.Add("Access-Control-Request-Headers", "accept,content-type");

        return client.SendAsync(request);
    }

    private static string? SingleHeader(HttpResponseMessage response, string name) =>
        response.Headers.TryGetValues(name, out var values) ? Assert.Single(values) : null;
}
