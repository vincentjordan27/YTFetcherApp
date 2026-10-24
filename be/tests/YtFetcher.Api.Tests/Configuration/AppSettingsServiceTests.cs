using Microsoft.Extensions.Configuration;
using YtFetcher.Infrastructure.Services;

namespace YtFetcher.Api.Tests.Configuration;

public class AppSettingsServiceTests
{
    [Fact]
    public void GetRequiredValue_returns_the_configured_value()
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["ConnectionStrings:Postgres"] = "Host=localhost;Database=yt_fetcher"
            })
            .Build();

        var service = new AppSettingsService(configuration);

        var value = service.GetRequiredValue("ConnectionStrings:Postgres");

        Assert.Equal("Host=localhost;Database=yt_fetcher", value);
    }

    [Fact]
    public void GetRequiredValue_throws_when_the_key_is_missing()
    {
        var configuration = new ConfigurationBuilder().Build();
        var service = new AppSettingsService(configuration);

        var exception = Assert.Throws<InvalidOperationException>(
            () => service.GetRequiredValue("ConnectionStrings:Postgres"));

        Assert.Contains("ConnectionStrings:Postgres", exception.Message);
    }
}
