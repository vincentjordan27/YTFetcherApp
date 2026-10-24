using YtFetcher.Core.Videos;

namespace YtFetcher.Application.Videos.DTO.Response;

/// <summary>
/// One active-video row. Values are raw: formatting duration, compact numbers, and browser-timezone
/// dates belongs to the client. The provider channel id travels with the row so the client can build a
/// channel link from the id instead of guessing one from the display name.
/// </summary>
public sealed record ActiveVideoDto(
    Guid Id,
    string YoutubeVideoId,
    string Title,
    string ChannelName,
    string YoutubeChannelId,
    string? ThumbnailUrl,
    long DurationMillis,
    long CurrentViews,
    long CurrentLikes,
    DateTimeOffset PublishedAt,
    DateTimeOffset? LastStatsFetchedAt)
{
    public static ActiveVideoDto ToDto(Video video, string channelName, string youtubeChannelId) => new(
        video.Id,
        video.YoutubeVideoId,
        video.Title,
        channelName,
        youtubeChannelId,
        video.ThumbnailUrl,
        video.DurationMillis,
        video.CurrentViews,
        video.CurrentLikes,
        video.PublishedAt,
        video.LastStatsFetchedAt);
}
