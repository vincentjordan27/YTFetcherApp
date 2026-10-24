namespace YtFetcher.Application.Discovery.DTO.Result;

/// <summary>
/// One discovery candidate: channel metadata plus the video that revealed it.
/// </summary>
public sealed record YouTubeChannelCandidate(
    string YouTubeChannelId,
    string ChannelName,
    string? ChannelThumbnailUrl,
    string? UploadsPlaylistId,
    string YouTubeVideoId,
    string VideoTitle,
    string? VideoThumbnailUrl,
    DateTimeOffset VideoPublishedAt);
