namespace YtFetcher.Application.Discovery.DTO.Result;

/// <summary>One validated discovery search item carrying the video that revealed a channel.</summary>
public sealed record YouTubeDiscoverySearchItem(
    string YouTubeChannelId,
    string YouTubeVideoId,
    string VideoTitle,
    string? VideoThumbnailUrl,
    DateTimeOffset VideoPublishedAt);
