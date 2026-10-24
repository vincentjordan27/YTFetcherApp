namespace YtFetcher.Application.Abstractions.Services;

/// <summary>
/// Environment quota guard for how many pages one channel may commit. It is not a product boundary:
/// the product boundary is always the frozen cutoff. A positive value is the maximum number of
/// committed pages per channel, including page one; <c>0</c> means no artificial page-count limit.
/// Application cannot read configuration, so this port carries the configured value inward.
/// </summary>
public interface IBackfillPageLimitPolicy
{
    /// <summary>Configured positive page limit, or <c>0</c> when no artificial limit applies.</summary>
    int PageLimit { get; }
}
