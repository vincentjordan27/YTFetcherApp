namespace YtFetcher.Core.Quotas;

/// <summary>
/// Outcome of an attempt to take durable ownership of one Pacific quota date's daily processing run. Only
/// <see cref="Claimed"/> may run the daily phases and open the backfill dispatch gate.
/// </summary>
public enum DailyProcessClaimDecision
{
    /// <summary>This attempt now owns the date and is the only run allowed to do the daily work.</summary>
    Claimed,

    /// <summary>The date already finished; nothing may run again for it.</summary>
    AlreadyCompleted,

    /// <summary>A live claim owns the date, so this attempt must not do any provider or dispatch work.</summary>
    AlreadyRunning
}
