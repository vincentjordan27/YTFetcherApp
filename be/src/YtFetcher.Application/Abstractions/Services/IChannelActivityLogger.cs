using YtFetcher.Core.Channels;

namespace YtFetcher.Application.Abstractions.Services;

/// <summary>
/// Observability boundary for successful channel lifecycle mutations. Implemented in Infrastructure.
/// </summary>
public interface IChannelActivityLogger
{
    void LogApproved(Channel channel, ChannelStatus previousStatus);

    void LogRejected(Channel channel, ChannelStatus previousStatus);

    void LogReopened(Channel channel);

    void LogSoftDeleted(Channel channel, ChannelStatus previousStatus);

    /// <summary>
    /// An operator retried terminally failed page work. The channel returns to the ordinary dispatch path
    /// with its cursor, committed count and cutoff intact.
    /// </summary>
    void LogBackfillRetryRequested(Channel channel, BackfillStatus previousBackfillStatus);
}
