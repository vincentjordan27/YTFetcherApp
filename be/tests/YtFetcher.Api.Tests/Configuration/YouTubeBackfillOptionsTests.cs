using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using YtFetcher.Infrastructure;
using YtFetcher.Infrastructure.Configuration;

namespace YtFetcher.Api.Tests.Configuration;

/// <summary>
/// The backfill quota limits are startup requirements: <c>AddInfrastructure</c>
/// must stop with a readable configuration error instead of failing later inside a job.
/// </summary>
public class YouTubeBackfillOptionsTests
{
    [Fact]
    public void Valid_backfill_settings_pass_startup_validation()
    {
        Assert.Null(Record.Exception(() => AddInfrastructure(CreateConfiguration())));
    }

    [Theory]
    [InlineData("0")]
    [InlineData("-1")]
    [InlineData("not-a-number")]
    public void A_non_positive_max_daily_approved_channels_stops_startup(string value)
    {
        var configuration = CreateConfiguration(("YouTube:MaxDailyApprovedChannels", value));

        var exception = Assert.Throws<InvalidOperationException>(() => AddInfrastructure(configuration));

        Assert.Contains("MaxDailyApprovedChannels", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void A_max_daily_approved_channels_above_either_bucket_limit_stops_startup()
    {
        var configuration = CreateConfiguration(("YouTube:MaxDailyApprovedChannels", "20000"));

        var exception = Assert.Throws<InvalidOperationException>(() => AddInfrastructure(configuration));

        Assert.Contains("MaxDailyApprovedChannels", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void A_missing_queries_daily_quota_limit_stops_startup()
    {
        var configuration = CreateConfiguration(("YouTube:QueriesDailyQuotaLimit", null));

        var exception = Assert.Throws<InvalidOperationException>(() => AddInfrastructure(configuration));

        Assert.Contains("QueriesDailyQuotaLimit", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Missing_page_limit_stops_startup_with_a_readable_error()
    {
        var configuration = CreateConfiguration(("YouTube:BackfillPageLimit", null));

        var exception = Assert.Throws<InvalidOperationException>(() => AddInfrastructure(configuration));

        Assert.Contains("YouTube:BackfillPageLimit", exception.Message, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("-1")]
    [InlineData("not-a-number")]
    [InlineData("2.5")]
    public void A_page_limit_that_is_not_a_whole_number_of_zero_or_more_stops_startup(string value)
    {
        var configuration = CreateConfiguration(("YouTube:BackfillPageLimit", value));

        var exception = Assert.Throws<InvalidOperationException>(() => AddInfrastructure(configuration));

        Assert.Contains("YouTube:BackfillPageLimit", exception.Message, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("not-a-number")]
    [InlineData("0")]
    [InlineData("-1")]
    [InlineData("2.5")]
    public void A_missing_or_malformed_dispatch_batch_size_stops_startup(string value)
    {
        var configuration = CreateConfiguration(("YouTube:BackfillDispatchBatchSize", value));

        var exception = Assert.Throws<InvalidOperationException>(() => AddInfrastructure(configuration));

        Assert.Contains("YouTube:BackfillDispatchBatchSize", exception.Message, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("0")]
    [InlineData("3")]
    public void Zero_and_positive_page_limits_pass_startup_validation(string value)
    {
        var configuration = CreateConfiguration(("YouTube:BackfillPageLimit", value));

        Assert.Null(Record.Exception(() => AddInfrastructure(configuration)));
    }

    [Theory]
    [InlineData("-1")]
    [InlineData("not-a-number")]
    [InlineData("2.5")]
    public void A_discovery_page_limit_that_is_not_a_whole_number_of_zero_or_more_stops_startup(string value)
    {
        var configuration = CreateConfiguration(("YouTube:DiscoveryMaxPagesPerKeywordPerRun", value));

        var exception = Assert.Throws<InvalidOperationException>(() => AddInfrastructure(configuration));

        Assert.Contains("YouTube:DiscoveryMaxPagesPerKeywordPerRun", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void A_missing_discovery_page_limit_stops_startup()
    {
        var configuration = CreateConfiguration(("YouTube:DiscoveryMaxPagesPerKeywordPerRun", null));

        var exception = Assert.Throws<InvalidOperationException>(() => AddInfrastructure(configuration));

        Assert.Contains("YouTube:DiscoveryMaxPagesPerKeywordPerRun", exception.Message, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("0")]
    [InlineData("1")]
    [InlineData("100")]
    public void Zero_and_positive_discovery_page_limits_pass_startup_validation(string value)
    {
        var configuration = CreateConfiguration(("YouTube:DiscoveryMaxPagesPerKeywordPerRun", value));

        Assert.Null(Record.Exception(() => AddInfrastructure(configuration)));
    }

    private static IServiceCollection AddInfrastructure(IConfiguration configuration)
    {
        var services = new ServiceCollection();

        // No provider is resolved here, so Hangfire storage is never opened. The authentication mode is
        // resolved the same way the composition root resolves it, which keeps this a startup-validation test.
        var authentication = AuthenticationRuntimeConfiguration.Load(
            configuration,
            new TestHostEnvironment(Environments.Development));

        services.AddInfrastructure(configuration, authentication);

        return services;
    }

    private static IConfiguration CreateConfiguration(
        params (string Key, string? Value)[] overrides)
    {
        var values = new Dictionary<string, string?>
        {
            ["Database:Host"] = "localhost",
            ["Database:Port"] = "5432",
            ["Database:Name"] = "yt_fetcher",
            ["Database:Username"] = "yt_fetcher_admin",
            ["Database:Password"] = "not-a-real-password",
            ["YouTube:QueriesDailyQuotaLimit"] = "10000",
            ["YouTube:VideoBatchGetStatsDailyQuotaLimit"] = "10000",
            ["YouTube:SearchQueriesDailyQuotaLimit"] = "100",
            ["YouTube:DiscoveryMaxPagesPerKeywordPerRun"] = "1",
            ["YouTube:MaxDailyApprovedChannels"] = "500",
            ["YouTube:BackfillPageLimit"] = "3",
            ["YouTube:BackfillDispatchBatchSize"] = "10"
        };

        foreach (var (key, value) in overrides)
        {
            values[key] = value;
        }

        return new ConfigurationBuilder().AddInMemoryCollection(values).Build();
    }
}
