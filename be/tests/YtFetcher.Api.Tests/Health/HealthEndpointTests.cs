using System.Net;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Mvc.Testing;

namespace YtFetcher.Api.Tests.Health;

[Collection(ApiStartupCollection.Name)]
public class HealthEndpointTests(TestApiFactory factory) : IClassFixture<TestApiFactory>
{
    [Fact]
    public async Task Get_health_returns_the_service_status()
    {
        using var databaseConfiguration = new HostConfigurationScope();
        using var application = factory.WithWebHostBuilder(_ => { });
        var client = application.CreateClient();

        var response = await client.GetAsync("/api/health");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var payload = await response.Content.ReadFromJsonAsync<HealthResponse>();

        Assert.NotNull(payload);
        Assert.Equal("ok", payload.Status);
        Assert.Equal("yt-fetcher", payload.Service);
    }

    private sealed record HealthResponse(string Status, string Service);
}
