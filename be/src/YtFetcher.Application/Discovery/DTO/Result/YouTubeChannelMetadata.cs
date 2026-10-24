namespace YtFetcher.Application.Discovery.DTO.Result;

/// <summary>Normalized channel metadata returned by one discovery metadata lookup.</summary>
public sealed record YouTubeChannelMetadata(
    string YouTubeChannelId,
    string ChannelName,
    string? ChannelThumbnailUrl,
    string? UploadsPlaylistId);
