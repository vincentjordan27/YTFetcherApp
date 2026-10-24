namespace YtFetcher.Core.Channels;

/// <summary>
/// Review lifecycle of a discovered channel. Member names match the database values.
/// </summary>
public enum ChannelStatus
{
    PENDING,
    APPROVED,
    REJECTED
}
