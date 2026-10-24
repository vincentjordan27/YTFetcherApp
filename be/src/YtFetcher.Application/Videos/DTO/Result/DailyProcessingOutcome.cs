namespace YtFetcher.Application.Videos.DTO.Result;

/// <summary>
/// How one daily processing trigger ended. A provider quota limit is an expected outcome: the run still
/// completes the date and opens no gate for an unavailable bucket, so it is never reported as a failure.
/// </summary>
public enum DailyProcessingOutcome
{
    /// <summary>The phases ran, the date was completed, and dispatch ran when both buckets allowed it.</summary>
    Completed,

    /// <summary>The date already finished; this trigger did nothing.</summary>
    AlreadyCompleted,

    /// <summary>A live daily-process claim owns the date; this trigger did nothing.</summary>
    AlreadyRunning,

    /// <summary>
    /// A daily-owner-fenced reservation found that this run no longer owns the date, so it stopped without a
    /// provider call, a marker, or finalization.
    /// </summary>
    OwnershipLost
}
