namespace YtFetcher.Application.Channels.DTO.Result;

/// <summary>
/// Outcome of an approval transaction that must also claim a paired first-page quota slot. The quota
/// refusals never change channel state, scheduling state, or a bucket counter.
/// </summary>
public enum ChannelApprovalStatus
{
    /// <summary>The slot was claimed and the channel lifecycle/scheduling mutation was applied.</summary>
    Succeeded,

    /// <summary>The channel does not exist or is soft deleted.</summary>
    NotFound,

    /// <summary>The channel is no longer pending, so the lifecycle transition is not allowed.</summary>
    NotPending,

    /// <summary>The duplicate policy found an existing durable row; it was returned unchanged.</summary>
    Duplicate,

    /// <summary>The date's first-page window was never initialized, so approval must be refused with 503.</summary>
    WindowNotReady,

    /// <summary>The configured daily approved-channel maximum is already reached, so approval is refused with 429.</summary>
    LimitReached
}
