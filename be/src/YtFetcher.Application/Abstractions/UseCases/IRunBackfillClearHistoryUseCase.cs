using YtFetcher.Application.Backfill.DTO.Result;

namespace YtFetcher.Application.Abstractions.UseCases;

/// <summary>
/// Application use-case boundary for the backfill run retention job. Clearing history is the explicit
/// exception to ordinary soft deletion: this use case physically removes old terminal run rows.
/// </summary>
public interface IRunBackfillClearHistoryUseCase
{
    /// <summary>
    /// Removes terminal run rows older than the retention window using bounded batches, and reports how
    /// much was cleared. Running rows are never removed.
    /// </summary>
    Task<BackfillClearHistoryResult> RunAsync(CancellationToken cancellationToken);
}
