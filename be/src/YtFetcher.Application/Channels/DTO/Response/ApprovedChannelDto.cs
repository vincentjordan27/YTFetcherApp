using YtFetcher.Core.Channels;
using CoreBackfillStatus = YtFetcher.Core.Channels.BackfillStatus;

namespace YtFetcher.Application.Channels.DTO.Response;

/// <summary>
/// One approved channel row. Presentation labels and date formatting belong to the client. The three
/// backfill failure fields are populated only while the channel's backfill status is
/// <c>FAILED</c>; a channel that never failed reports <c>0</c> and two nulls. The two scheduling
/// fields are populated only while the status is <c>SCHEDULING</c> or <c>SCHEDULING_FAILED</c>.
/// </summary>
public sealed record ApprovedChannelDto(
    Guid Id,
    string YoutubeChannelId,
    string Name,
    string? ThumbnailUrl,
    DateTimeOffset DiscoveredAt,
    DateTimeOffset? ApprovedAt,
    string BackfillStatus,
    DateTimeOffset? LastUploadCheckAt,
    int BackfillFailureCount,
    string? BackfillFailureReason,
    DateTimeOffset? BackfillLastFailedAt,
    DateTimeOffset? BackfillSchedulingAttemptedAt,
    string? BackfillSchedulingFailureReason)
{
    public static ApprovedChannelDto ToDto(Channel channel)
    {
        var isScheduling = channel.BackfillStatus is CoreBackfillStatus.SCHEDULING or CoreBackfillStatus.SCHEDULING_FAILED;
        var hasExecutionFailure = channel.BackfillStatus == CoreBackfillStatus.FAILED;

        return new(
            channel.Id,
            channel.YoutubeChannelId,
            channel.Name,
            channel.ThumbnailUrl,
            channel.DiscoveredAt,
            channel.ApprovedAt,
            channel.BackfillStatus.ToString(),
            channel.LastUploadCheckAt,
            // While the status is PENDING the count/reason/timestamp are internal retry data, so they
            // are never exposed until the third failure makes the execution failure visible.
            hasExecutionFailure ? channel.BackfillFailureCount : 0,
            hasExecutionFailure ? channel.BackfillFailureReason : null,
            hasExecutionFailure ? channel.BackfillLastFailedAt : null,
            isScheduling ? channel.BackfillSchedulingAttemptedAt : null,
            isScheduling ? channel.BackfillSchedulingFailureReason : null);
    }
}
