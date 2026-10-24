using YtFetcher.Core.Channels;

namespace YtFetcher.Application.Channels.DTO.Response;

/// <summary>One pending channel row with its real discovery evidence count.</summary>
public sealed record PendingChannelDto(
    Guid Id,
    string YoutubeChannelId,
    string Name,
    string? ThumbnailUrl,
    DateTimeOffset DiscoveredAt,
    int EvidenceCount)
{
    public static PendingChannelDto ToDto(Channel channel, int evidenceCount) => new(
        channel.Id,
        channel.YoutubeChannelId,
        channel.Name,
        channel.ThumbnailUrl,
        channel.DiscoveredAt,
        evidenceCount);
}
