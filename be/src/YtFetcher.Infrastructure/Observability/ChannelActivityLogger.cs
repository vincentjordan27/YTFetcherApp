using Microsoft.Extensions.Logging;
using YtFetcher.Application.Abstractions.Services;
using YtFetcher.Core.Channels;

namespace YtFetcher.Infrastructure.Observability;

public sealed class ChannelActivityLogger(ILogger<ChannelActivityLogger> logger) : IChannelActivityLogger
{
    public void LogApproved(Channel channel, ChannelStatus previousStatus) =>
        logger.LogInformation(
            "ChannelApproved {ChannelId} {YouTubeChannelId} {PreviousStatus} {CurrentStatus} {BackfillStatus}",
            channel.Id,
            channel.YoutubeChannelId,
            previousStatus,
            channel.Status,
            channel.BackfillStatus);

    public void LogRejected(Channel channel, ChannelStatus previousStatus) =>
        logger.LogInformation(
            "ChannelRejected {ChannelId} {YouTubeChannelId} {PreviousStatus} {CurrentStatus}",
            channel.Id,
            channel.YoutubeChannelId,
            previousStatus,
            channel.Status);

    public void LogReopened(Channel channel) =>
        logger.LogInformation(
            "ChannelReopened {ChannelId} {YouTubeChannelId} {CurrentStatus}",
            channel.Id,
            channel.YoutubeChannelId,
            channel.Status);

    public void LogBackfillRetryRequested(Channel channel, BackfillStatus previousBackfillStatus) =>
        logger.LogInformation(
            "ChannelBackfillRetryRequested {ChannelId} {PreviousBackfillStatus}",
            channel.Id,
            previousBackfillStatus);

    public void LogSoftDeleted(Channel channel, ChannelStatus previousStatus) =>
        logger.LogInformation(
            "ChannelSoftDeleted {ChannelId} {YouTubeChannelId} {PreviousStatus}",
            channel.Id,
            channel.YoutubeChannelId,
            previousStatus);
}
