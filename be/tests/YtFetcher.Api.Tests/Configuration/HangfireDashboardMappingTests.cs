using YtFetcher.Api.Configuration;

namespace YtFetcher.Api.Tests.Configuration;

public class HangfireDashboardMappingTests
{
    [Fact]
    public void Dashboard_is_mapped_when_explicitly_enabled()
    {
        Assert.True(HangfireDashboardMapping.ShouldMap(dashboardEnabled: true));
    }

    [Fact]
    public void Dashboard_is_not_mapped_when_disabled()
    {
        Assert.False(HangfireDashboardMapping.ShouldMap(dashboardEnabled: false));
    }
}
