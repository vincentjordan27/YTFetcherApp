using YtFetcher.Core.Channels;

namespace YtFetcher.Application.Abstractions.Services;

/// <summary>
/// Observability boundary for the approved-channel backfill. It carries operator-relevant failure and
/// degradation events only: a normal successful page, queue step, or provider response is durable database state
/// plus the daily job summary, not an event. Implemented in Infrastructure with structured, secret-free scalars.
/// </summary>
public interface IBackfillActivityLogger
{
    void LogFirstPageProviderFailed(Guid channelId, string operation, string reason);

    void LogFirstPageFailed(Guid channelId, int failureCount, int failureLimit, string reason);

    void LogFirstPageQuotaExceeded(Guid channelId, DateOnly quotaDate, string operation);

    /// <summary>
    /// The failure-state transaction itself could not commit, so no release happened and the live
    /// claim must expire naturally. The unexpected exception is rethrown after this log.
    /// </summary>
    void LogFailureStateWriteFailed(Guid channelId, string operation, Exception exception);

    /// <summary>
    /// The page-persistence transaction failed. The original exception is carried so the operator
    /// can diagnose it from the log; the persisted failure reason never contains it.
    /// </summary>
    void LogPersistenceFailed(Guid channelId, string operation, Exception exception);

    /// <summary>
    /// The queue enqueue call threw. The original exception is carried for the operator diagnostic;
    /// the persisted scheduling failure reason never contains it.
    /// </summary>
    void LogSchedulingFailed(Guid channelId, Exception exception);

    /// <summary>
    /// The short transaction that persists the scheduling outcome could not commit, so the row keeps
    /// its previous durable scheduling state and recovery must pick it up later.
    /// </summary>
    void LogSchedulingStateWriteFailed(Guid channelId, Exception exception);

    /// <summary>
    /// A page reached the consecutive execution-failure limit, so the channel is terminally failed and the
    /// pump stops selecting it. The cursor, committed count, cutoff and existing data are untouched.
    /// </summary>
    void LogTerminalPageFailure(Guid channelId, int failureCount, string reason);

    /// <summary>
    /// One retention pass removed expired run rows. The counts are scalars, so the line is stable and
    /// secret-free: no reason text, payload, or identifier is included.
    /// </summary>
    void LogRetentionCleared(int deletedCount, int batchCount);
}
