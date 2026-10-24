using System.Net;
using Microsoft.Extensions.Hosting;
using YtFetcher.Api.Configuration;

namespace YtFetcher.Api.Tests.Discovery;

[Collection(ApiStartupCollection.Name)]
public class HangfireDashboardHostTests
{
    [Fact]
    public async Task Dashboard_route_is_not_mapped_outside_development()
    {
        using var environment = new EnvironmentConfigurationScope(Environments.Production);
        using var databaseConfiguration = new HostConfigurationScope();
        using var factory = new TestApiFactory();
        using var client = factory.CreateClient();

        var response = await client.GetAsync(HangfireDashboardMapping.Path);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }
}
