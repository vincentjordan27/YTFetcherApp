using System.Net;
using Microsoft.Extensions.Hosting;

namespace YtFetcher.Api.Tests;

[Collection(ApiStartupCollection.Name)]
public class SwaggerHostTests
{
    [Fact]
    public async Task Swagger_ui_is_available_in_development()
    {
        using var environment = new EnvironmentConfigurationScope(Environments.Development);
        using var databaseConfiguration = new HostConfigurationScope();
        using var factory = new TestApiFactory();
        using var client = factory.CreateClient();

        var response = await client.GetAsync("/swagger/index.html");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }
}
