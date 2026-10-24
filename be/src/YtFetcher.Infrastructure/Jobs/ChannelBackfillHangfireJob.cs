using Hangfire;
using YtFetcher.Application.Abstractions.UseCases;
using YtFetcher.Core.BackfillRuns;

namespace YtFetcher.Infrastructure.Jobs;

/// <summary>
/// Hangfire adapter for one approved channel page. It only invokes the backfill use case once per run;
/// expected provider and quota outcomes are handled by the use case. There is exactly one page worker, so
/// this job serves page one, a continuation, and the retry of a failed page, and the trigger source is
/// carried into the durable run history. A committed execution failure below the failure limit surfaces
/// as a retry request, so this job retries at most twice (three runs total) with a 15 minute delay each,
/// which is never shorter than the page-claim lifetime.
/// </summary>
public sealed class ChannelBackfillHangfireJob(IBackfillUseCase backfillUseCase)
{
    [AutomaticRetry(Attempts = 2, DelaysInSeconds = new[] { 900, 900 })]
    public Task RunPageAsync(
        Guid channelId,
        BackfillTriggerSource triggerSource,
        CancellationToken cancellationToken) =>
        backfillUseCase.RunPageAsync(channelId, triggerSource, cancellationToken);
}
