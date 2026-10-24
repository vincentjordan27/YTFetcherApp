namespace YtFetcher.Core.Channels;

/// <summary>
/// Backfill progress of an approved channel. Member names match the database values.
/// </summary>
public enum BackfillStatus
{
    PENDING,
    RUNNING,
    COMPLETED,
    FAILED,

    /// <summary>
    /// Approval has committed and an enqueue attempt is in progress or must be recovered. There is
    /// no guarantee a Hangfire job exists yet.
    /// </summary>
    SCHEDULING,

    /// <summary>
    /// An enqueue attempt threw and the safe scheduling failure was persisted. This is not one of the
    /// three YouTube/provider/persistence backfill execution failures.
    /// </summary>
    SCHEDULING_FAILED
}
