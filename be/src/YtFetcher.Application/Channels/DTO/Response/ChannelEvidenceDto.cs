using YtFetcher.Core.Discovery;

namespace YtFetcher.Application.Channels.DTO.Response;

/// <summary>
/// One discovery evidence row: the video that revealed the channel and the keyword that found it.
/// </summary>
public sealed record ChannelEvidenceDto(
    string YoutubeVideoId,
    string Title,
    string? ThumbnailUrl,
    DateTimeOffset PublishedAt,
    string Keyword)
{
    /// <summary>Fallback used when the discovery keyword is missing, inactive, or soft deleted.</summary>
    public const string UnknownKeyword = "Unknown keyword";

    public static ChannelEvidenceDto ToDto(ChannelDiscovery discovery, string? keyword) => new(
        discovery.YoutubeVideoId,
        discovery.VideoTitle,
        discovery.VideoThumbnailUrl,
        discovery.VideoPublishedAt,
        string.IsNullOrWhiteSpace(keyword) ? UnknownKeyword : keyword);
}
