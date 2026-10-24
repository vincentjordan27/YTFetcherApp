namespace YtFetcher.Core.Videos;

/// <summary>
/// Whether the provider still serves a formerly tracked video. Availability is deliberately separate from
/// <see cref="VideoTrackingStatus"/>: tracking says whether polling is on, availability explains why a
/// tracked video can no longer be polled.
/// </summary>
/// <remarks>
/// <c>PRIVATE</c> is intentionally absent. The current provider contract
/// (<c>VideoStatsItemResult</c> and the <c>videos:batchGetStats</c> route) carries no privacy field, so a
/// private video cannot be distinguished from a deleted one and is recorded as
/// <see cref="UNAVAILABLE"/>. A private state is added once the gateway genuinely exposes it.
/// </remarks>
public enum VideoAvailabilityStatus
{
    AVAILABLE,
    UNAVAILABLE
}
