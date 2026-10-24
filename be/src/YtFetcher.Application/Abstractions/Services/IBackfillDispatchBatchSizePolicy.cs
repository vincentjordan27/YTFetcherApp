namespace YtFetcher.Application.Abstractions.Services;

/// <summary>
/// Scheduling bound for one daily processing run: the maximum number of channel-page workers it dispatches. It
/// is not a quota setting, and Application must not read configuration to obtain it, so this port carries the
/// already-validated value inward exactly like <see cref="IBackfillPageLimitPolicy"/> carries the page limit.
/// </summary>
public interface IBackfillDispatchBatchSizePolicy
{
    /// <summary>Configured positive dispatch bound for one daily run.</summary>
    int BatchSize { get; }
}
