namespace YtFetcher.Application.Quotas;

/// <summary>
/// Credential-free configuration key names for the daily provider quota buckets. They live in Application so
/// the Usage projection can report the configured limits through the existing settings port without
/// Infrastructure leaking its option shapes upward.
/// </summary>
public static class QuotaConfigurationKeys
{
    public const string QueriesDailyQuotaLimit = "YouTube:QueriesDailyQuotaLimit";

    public const string VideoBatchGetStatsDailyQuotaLimit = "YouTube:VideoBatchGetStatsDailyQuotaLimit";

    public const string SearchQueriesDailyQuotaLimit = "YouTube:SearchQueriesDailyQuotaLimit";

    public const string DiscoveryMaxPagesPerKeywordPerRun = "YouTube:DiscoveryMaxPagesPerKeywordPerRun";

    public const string MaxDailyApprovedChannels = "YouTube:MaxDailyApprovedChannels";
}
