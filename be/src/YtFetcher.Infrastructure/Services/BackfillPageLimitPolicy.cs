using System.Globalization;
using YtFetcher.Application.Abstractions.Services;

namespace YtFetcher.Infrastructure.Services;

/// <summary>
/// Bind-and-validate reader for the environment page-count guard. The value is validated once while the
/// host is being built, so a missing or nonsensical setting stops startup with a readable sentence
/// instead of silently disabling the local cap or failing later inside a page job.
/// </summary>
public sealed class BackfillPageLimitPolicy : IBackfillPageLimitPolicy
{
    /// <summary>Configuration key, kept beside the policy that owns its meaning.</summary>
    public const string PageLimitSetting = "YouTube:BackfillPageLimit";

    public BackfillPageLimitPolicy(IAppSettingsService appSettingsService)
    {
        var value = appSettingsService.GetValue(PageLimitSetting);

        if (string.IsNullOrWhiteSpace(value))
        {
            throw new InvalidOperationException($"Configuration value '{PageLimitSetting}' is missing.");
        }

        if (!int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsed) ||
            parsed < 0)
        {
            throw new InvalidOperationException(
                $"Configuration value '{PageLimitSetting}' must be a whole number of zero or more.");
        }

        PageLimit = parsed;
    }

    public int PageLimit { get; }
}
