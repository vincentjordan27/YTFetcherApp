namespace YtFetcher.Core.BackfillRuns;

/// <summary>
/// Lifecycle of one persisted backfill page attempt. Member names match the database check-constraint
/// values exactly.
/// </summary>
public enum BackfillRunStatus
{
    /// <summary>The page attempt owns its claim and has not reached a terminal state yet.</summary>
    RUNNING,

    /// <summary>The page committed successfully and advanced or completed the channel.</summary>
    SUCCEEDED,

    /// <summary>The page ended in an expected execution failure that was recorded as readable state.</summary>
    FAILED,

    /// <summary>
    /// The page stopped because a provider quota bucket was unavailable for the current Pacific date. It is
    /// an expected limit, not an execution failure, so it carries no failure reason/count and is not offered
    /// for retry on the same date.
    /// </summary>
    QUOTA_LIMIT_REACHED,

    /// <summary>
    /// A recovery pass found the attempt's claim stale, so the row was closed without a provider or
    /// persistence outcome. The claim owner may still be running and must not mutate channel state.
    /// </summary>
    ABANDONED
}
