using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Hosting.Internal;
using YtFetcher.Api.Configuration;

namespace YtFetcher.Api.Tests.Configuration;

public class HangfireDashboardMappingTests
{
    [Fact]
    public void Dashboard_is_mapped_in_development()
    {
        Assert.True(HangfireDashboardMapping.ShouldMap(Environment(Environments.Development)));
    }

    [Theory]
    [InlineData("Production")]
    [InlineData("Staging")]
    [InlineData("")]
    public void Dashboard_is_not_mapped_outside_development(string environmentName)
    {
        Assert.False(HangfireDashboardMapping.ShouldMap(Environment(environmentName)));
    }

    private static IHostEnvironment Environment(string environmentName) =>
        new HostingEnvironment { EnvironmentName = environmentName };
}
