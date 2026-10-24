using YtFetcher.Core.BackfillRuns;

namespace YtFetcher.Application.Abstractions.UseCases;

/// <summary>
/// The single queue-enqueue boundary for a channel that is already durably in
/// <c>backfill_status = SCHEDULING</c>. Approval, manual add, the dispatch pump, and stale recovery all
/// route through this one application operation, so enqueue and error-transition logic is never duplicated
/// and only this operation ever calls the scheduler port.
/// </summary>
public interface IBackfillSchedulingUseCase
{
    /// <summary>
    /// Enqueues exactly one page job for the channel with the reason it was dispatched, then persists the
    /// resulting scheduling state in a second short transaction. A non-cancellation enqueue exception is
    /// logged and persisted as the safe <c>SCHEDULING_FAILED</c> reason; a persistence failure leaves the
    /// durable state untouched for recovery, which owns it once the lease is stale. A failed enqueue is never
    /// rethrown, so one channel cannot stop a bounded batch.
    /// </summary>
    Task ScheduleAsync(
        Guid channelId,
        BackfillTriggerSource triggerSource,
        CancellationToken cancellationToken);
}
