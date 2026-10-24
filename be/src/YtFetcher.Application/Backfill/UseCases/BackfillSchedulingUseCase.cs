using YtFetcher.Application.Abstractions.Repositories;
using YtFetcher.Application.Abstractions.Services;
using YtFetcher.Application.Abstractions.UseCases;
using YtFetcher.Core.BackfillRuns;

namespace YtFetcher.Application.Backfill.UseCases;

/// <summary>
/// One queue-enqueue attempt for a channel that is already durably <c>SCHEDULING</c>. It owns the
/// enqueue call and the failure-state write only; it never holds a database lock while calling Hangfire,
/// and it deliberately never writes <c>SCHEDULING -&gt; PENDING</c> after a successful enqueue. That
/// write is a race: a fast worker could claim the row as <c>PENDING</c>, and if the transition then
/// landed it would move the row out from under the running job. The enqueued worker itself performs the
/// <c>SCHEDULING -&gt; RUNNING</c> claim, so the durable row stays usable even when the process dies
/// immediately after the enqueue.
/// </summary>
public sealed class BackfillSchedulingUseCase(
    IChannelRepository channelRepository,
    IBackfillScheduler backfillScheduler,
    IBackfillActivityLogger backfillActivityLogger,
    TimeProvider timeProvider) : IBackfillSchedulingUseCase
{
    /// <summary>Exact operator-facing sentence persisted for any failed enqueue attempt.</summary>
    public const string SafeSchedulingFailureReason = "Backfill could not be scheduled. Try again.";

    public async Task ScheduleAsync(
        Guid channelId,
        BackfillTriggerSource triggerSource,
        CancellationToken cancellationToken)
    {
        try
        {
            backfillScheduler.EnqueuePage(channelId, triggerSource);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            // The original exception is for the log only; the persisted reason is always the fixed,
            // readable sentence.
            backfillActivityLogger.LogSchedulingFailed(channelId, exception);
            await PersistSchedulingFailureAsync(channelId, cancellationToken);
            return;
        }

    }

    private async Task PersistSchedulingFailureAsync(Guid channelId, CancellationToken cancellationToken)
    {
        try
        {
            await channelRepository.TryFailSchedulingAsync(
                channelId,
                SafeSchedulingFailureReason,
                timeProvider.GetUtcNow(),
                cancellationToken);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            // The failure-state write did not commit either. The row keeps the SCHEDULING state from
            // the approval commit, and recovery owns it later.
            backfillActivityLogger.LogSchedulingStateWriteFailed(channelId, exception);
        }
    }
}
