namespace YtFetcher.Core.BackfillRuns;

/// <summary>
/// Why a backfill page attempt was dispatched. Member names match the database check-constraint
/// values exactly.
/// </summary>
public enum BackfillTriggerSource
{
    /// <summary>An approval or manual add scheduled the channel's first page through the approval path.</summary>
    APPROVAL,

    /// <summary>The scheduled dispatch pump selected the channel from its durable ready time.</summary>
    DAILY_PUMP,

    /// <summary>
    /// A recovery pass re-enqueued a channel whose scheduling or running state was abandoned, so the
    /// attempt is a recovery of earlier work rather than a fresh selection.
    /// </summary>
    RECOVERY
}
