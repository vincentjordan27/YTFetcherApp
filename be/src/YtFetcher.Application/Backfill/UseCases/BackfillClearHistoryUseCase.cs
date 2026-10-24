using YtFetcher.Application.Abstractions.Repositories;
using YtFetcher.Application.Abstractions.Services;
using YtFetcher.Application.Abstractions.UseCases;
using YtFetcher.Application.Backfill.DTO.Result;

namespace YtFetcher.Application.Backfill.UseCases;

/// <summary>
/// Clears expired backfill run history. Retention is the explicit exception to ordinary soft deletion:
/// old terminal rows are physically removed so the history table cannot grow forever. The work is
/// bounded per invocation so one scheduled run stays short and a large backlog is cleared over several
/// runs instead of one long transaction.
/// </summary>
public sealed class BackfillClearHistoryUseCase(
    IBackfillRunRepository backfillRunRepository,
    IBackfillActivityLogger backfillActivityLogger,
    TimeProvider timeProvider) : IRunBackfillClearHistoryUseCase
{
    /// <summary>How long terminal run history is kept before it becomes eligible for physical removal.</summary>
    public const int RetentionDays = 90;

    /// <summary>Maximum rows removed in one delete statement.</summary>
    public const int BatchSize = 500;

    /// <summary>Maximum batches one invocation may run, so the job has a hard upper bound.</summary>
    public const int MaximumBatches = 20;

    public async Task<BackfillClearHistoryResult> RunAsync(CancellationToken cancellationToken)
    {
        var startedBeforeUtc = timeProvider.GetUtcNow().AddDays(-RetentionDays);

        var deletedCount = 0;
        var batchCount = 0;
        var lastBatchWasFull = false;

        while (batchCount < MaximumBatches)
        {
            var removed = await backfillRunRepository.DeleteTerminalRunsStartedBeforeAsync(
                startedBeforeUtc,
                BatchSize,
                cancellationToken);

            deletedCount += removed;
            batchCount++;
            lastBatchWasFull = removed == BatchSize;

            // A short batch means the eligible rows are exhausted, so there is nothing left to clear.
            if (!lastBatchWasFull)
            {
                break;
            }
        }

        // One structured line only when history was actually cleared, so an empty pass stays silent.
        if (deletedCount > 0)
        {
            backfillActivityLogger.LogRetentionCleared(deletedCount, batchCount);
        }

        return new BackfillClearHistoryResult(
            deletedCount,
            batchCount,
            lastBatchWasFull && batchCount == MaximumBatches);
    }
}
