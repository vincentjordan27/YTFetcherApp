using System.Globalization;
using YtFetcher.Application.Abstractions.Services;
using YtFetcher.Application.Quotas;

namespace YtFetcher.Infrastructure.Services;

/// <summary>Reads and validates the per-keyword page cap for one channel-discovery run.</summary>
public sealed class DiscoveryPageLimitPolicy : IDiscoveryPageLimitPolicy
{
    public DiscoveryPageLimitPolicy(IAppSettingsService appSettingsService)
    {
        var value = appSettingsService.GetValue(QuotaConfigurationKeys.DiscoveryMaxPagesPerKeywordPerRun);

        if (!int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsed) || parsed < 0)
        {
            throw new InvalidOperationException(
                $"Configuration value '{QuotaConfigurationKeys.DiscoveryMaxPagesPerKeywordPerRun}' must be a whole number of zero or more.");
        }

        PageLimit = parsed;
    }

    public int PageLimit { get; }
}
