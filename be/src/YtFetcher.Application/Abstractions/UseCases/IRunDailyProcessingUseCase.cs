using YtFetcher.Application.Videos.DTO.Result;

namespace YtFetcher.Application.Abstractions.UseCases;

/// <summary>
/// The one daily processing operation. A recurring or manually triggered Hangfire run calls it once, and it
/// owns the ordered daily phases: ledger setup, tracked-video statistics polling, quota finalization and gate
/// opening, and one bounded channel dispatch pass.
/// </summary>
public interface IRunDailyProcessingUseCase
{
    /// <summary>
    /// Runs the ordered daily phases for the current Pacific quota date. A provider failure that is not a
    /// quota outcome throws <see cref="UseCases.DailyProcessingFailedException"/> so the caller marks the run
    /// failed instead of continuing to quota finalization, gate opening, or backfill dispatch. A provider
    /// <c>quotaExceeded</c> is a terminal daily outcome and returns normally with the backfill allocations
    /// exhausted and the gate left closed.
    /// </summary>
    Task<DailyProcessingResult> RunAsync(CancellationToken cancellationToken);
}
