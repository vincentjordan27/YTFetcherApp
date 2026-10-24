using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using YtFetcher.Api.Tests.Channels;
using YtFetcher.Application.Abstractions.Repositories;
using YtFetcher.Application.Abstractions.Services;

namespace YtFetcher.Api.Tests.Authentication;

/// <summary>
/// The intentionally anonymous deployment. It must keep the existing product API behavior, register no
/// Firebase or JWT requirement, and still answer the auth session endpoint with a readable state.
/// </summary>
[Collection(ApiStartupCollection.Name)]
public class DisabledAuthenticationHostTests
{
    [Fact]
    public async Task Session_reports_disabled_and_a_session_exchange_is_a_readable_conflict()
    {
        using var environment = new EnvironmentConfigurationScope(Environments.Development);
        using var hostConfiguration = new HostConfigurationScope();
        using var factory = new ChannelApiFactory();
        using var client = factory.CreateClient();

        var current = await client.GetAsync("/api/auth/session");

        using var currentDocument = JsonDocument.Parse(await current.Content.ReadAsStringAsync());
        Assert.False(currentDocument.RootElement.GetProperty("authenticationEnabled").GetBoolean());
        Assert.False(currentDocument.RootElement.GetProperty("authenticated").GetBoolean());

        var exchange = await client.PostAsJsonAsync(
            "/api/auth/session",
            new { firebaseIdToken = "any-token" });

        Assert.Equal(HttpStatusCode.Conflict, exchange.StatusCode);
        Assert.False(exchange.Headers.Contains("Set-Cookie"));

        using var problem = JsonDocument.Parse(await exchange.Content.ReadAsStringAsync());
        Assert.Contains(
            "disabled",
            problem.RootElement.GetProperty("detail").GetString()!,
            StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Protected_product_endpoints_stay_anonymous()
    {
        using var environment = new EnvironmentConfigurationScope(Environments.Development);
        using var hostConfiguration = new HostConfigurationScope();
        using var factory = new ChannelApiFactory();
        using var client = factory.CreateClient();

        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync("/api/usage/today")).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync("/api/videos/active/count")).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync("/api/health")).StatusCode);
    }

    [Fact]
    public async Task Logout_is_available_and_idempotent_without_a_cookie()
    {
        using var environment = new EnvironmentConfigurationScope(Environments.Development);
        using var hostConfiguration = new HostConfigurationScope();
        using var factory = new ChannelApiFactory();
        using var client = factory.CreateClient();

        var first = await client.PostAsJsonAsync("/api/auth/logout", new { });
        var second = await client.PostAsJsonAsync("/api/auth/logout", new { });

        Assert.Equal(HttpStatusCode.NoContent, first.StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, second.StatusCode);
    }

    [Fact]
    public void No_firebase_jwt_or_user_persistence_service_is_registered()
    {
        using var environment = new EnvironmentConfigurationScope(Environments.Development);
        using var hostConfiguration = new HostConfigurationScope();
        using var factory = new ChannelApiFactory();

        // Resolving the host also proves that a disabled instance starts with no Firebase section at all.
        _ = factory.Services;

        Assert.Null(factory.Services.GetService<IFirebaseIdentityVerifier>());
        Assert.Null(factory.Services.GetService<IApplicationSessionTokenIssuer>());
        Assert.Null(factory.Services.GetService<IApplicationSessionTokenValidator>());
        using var scope = factory.Services.CreateScope();

        Assert.Null(scope.ServiceProvider.GetService<IUserRepository>());

        var runtimeConfiguration = factory.Services.GetRequiredService<IAuthenticationRuntimeConfiguration>();

        Assert.False(runtimeConfiguration.AuthenticationEnabled);
    }
}
