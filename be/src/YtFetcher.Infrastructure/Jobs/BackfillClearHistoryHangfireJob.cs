using Hangfire;
using YtFetcher.Application.Abstractions.UseCases;

namespace YtFetcher.Infrastructure.Jobs;

/// <summary>
/// Recurring Hangfire adapter for clearing expired backfill run history. It calls one Application use case
/// once, and automatic retry is disabled by the delegation because the next daily run is the recovery
/// mechanism for a bounded cleanup.
/// </summary>
public sealed class BackfillClearHistoryHangfireJob(IRunBackfillClearHistoryUseCase clearHistoryUseCase)
{
    [AutomaticRetry(Attempts = 0)]
    public Task RunAsync(CancellationToken cancellationToken) =>
        clearHistoryUseCase.RunAsync(cancellationToken);
}
