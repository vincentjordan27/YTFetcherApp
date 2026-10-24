namespace YtFetcher.Core.Videos;

/// <summary>
/// Statistics tracking lifecycle of an ingested video. Member names match the database values.
/// </summary>
public enum VideoTrackingStatus
{
    ACTIVE,
    STOPPED
}
