using System.ComponentModel.DataAnnotations.Schema;
using YtFetcher.Core.Discovery;

namespace YtFetcher.Api.Tests.Core;

public sealed class DiscoveryStateDomainTests
{
    [Fact]
    public void Terminal_completion_marker_maps_to_the_pacific_quota_date_column()
    {
        var property = typeof(DiscoveryState).GetProperty(nameof(DiscoveryState.RecentCompletedQuotaDate));

        Assert.NotNull(property);
        Assert.Equal(typeof(DateOnly?), property!.PropertyType);

        var column = property.GetCustomAttributes(typeof(ColumnAttribute), inherit: false)
            .Cast<ColumnAttribute>()
            .Single();

        Assert.Equal("recent_completed_quota_date", column.Name);
    }
}
