namespace YtFetcher.Core.Videos;

/// <summary>The single curation state that determines whether a video is Active or in Reference.</summary>
public enum VideoReferenceStatus
{
    NONE,
    UNUSED,
    USED,
    SKIPPED
}
