using YtFetcher.Core.Channels;
using CoreBackfillStatus = YtFetcher.Core.Channels.BackfillStatus;

namespace YtFetcher.Application.Channels.DTO.Response;

/// <summary>Single channel read model used by the detail and mutation responses.</summary>
public sealed record ChannelDetailDto(
    Guid Id,
    string YoutubeChannelId,
    string Name,
    string? ThumbnailUrl,
    string Status,
    string? UploadsPlaylistId,
    string BackfillStatus,
    DateTimeOffset DiscoveredAt,
    DateTimeOffset? ApprovedAt,
    DateTimeOffset? RejectedAt,
    DateTimeOffset? LastUploadCheckAt,
    DateTimeOffset CreatedAt,
    DateTimeOffset ModifiedAt,
    DateTimeOffset? BackfillSchedulingAttemptedAt,
    string? BackfillSchedulingFailureReason)
{
    public static ChannelDetailDto ToDetail(Channel channel)
    {
        var isScheduling = channel.BackfillStatus is CoreBackfillStatus.SCHEDULING or CoreBackfillStatus.SCHEDULING_FAILED;

        return new(
            channel.Id,
            channel.YoutubeChannelId,
            channel.Name,
            channel.ThumbnailUrl,
            channel.Status.ToString(),
            channel.UploadsPlaylistId,
            channel.BackfillStatus.ToString(),
            channel.DiscoveredAt,
            channel.ApprovedAt,
            channel.RejectedAt,
            channel.LastUploadCheckAt,
            channel.CreatedAt,
            channel.ModifiedAt,
            isScheduling ? channel.BackfillSchedulingAttemptedAt : null,
            isScheduling ? channel.BackfillSchedulingFailureReason : null);
    }
}
