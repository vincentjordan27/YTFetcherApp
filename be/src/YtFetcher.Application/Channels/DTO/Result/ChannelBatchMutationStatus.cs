namespace YtFetcher.Application.Channels.DTO.Result;

public enum ChannelBatchMutationStatus
{
    Succeeded,
    NotFound,
    InvalidStatus,
    WindowNotReady,
    LimitReached
}
