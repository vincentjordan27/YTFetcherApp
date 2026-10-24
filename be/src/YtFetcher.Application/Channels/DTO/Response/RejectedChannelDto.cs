using YtFetcher.Core.Channels;

namespace YtFetcher.Application.Channels.DTO.Response;

/// <summary>One rejected channel row.</summary>
public sealed record RejectedChannelDto(
    Guid Id,
    string YoutubeChannelId,
    string Name,
    string? ThumbnailUrl,
    DateTimeOffset DiscoveredAt,
    DateTimeOffset? RejectedAt,
    string? RejectionReason)
{
    public static RejectedChannelDto ToDto(Channel channel) => new(
        channel.Id,
        channel.YoutubeChannelId,
        channel.Name,
        channel.ThumbnailUrl,
        channel.DiscoveredAt,
        channel.RejectedAt,
        channel.RejectionReason);
}
