using System.Globalization;
using YtFetcher.Application.Abstractions.Services;

namespace YtFetcher.Infrastructure.Services;

/// <summary>
/// Reads the daily dispatch bound from the validated <c>YouTube:BackfillDispatchBatchSize</c> setting and hands
/// Application only the resulting integer, exactly like <see cref="BackfillPageLimitPolicy"/> does for the page
/// limit. Infrastructure is the only layer that touches configuration.
/// </summary>
public sealed class BackfillDispatchBatchSizePolicy : IBackfillDispatchBatchSizePolicy
{
    public const string BatchSizeSetting = "YouTube:BackfillDispatchBatchSize";

    public BackfillDispatchBatchSizePolicy(IAppSettingsService appSettingsService)
    {
        var value = appSettingsService.GetValue(BatchSizeSetting);

        if (!int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsed) || parsed < 1)
        {
            throw new InvalidOperationException(
                $"Configuration value '{BatchSizeSetting}' is missing or is not a positive whole number.");
        }

        BatchSize = parsed;
    }

    public int BatchSize { get; }
}
