using Hangfire;
using YtFetcher.Application.Abstractions.UseCases;
using YtFetcher.Core.BackfillRuns;

namespace YtFetcher.Infrastructure.Jobs;

/// <summary>
/// Hangfire adapter for one approved channel page. Expected provider and quota outcomes are handled by the use
/// case. A committed execution failure below the failure limit surfaces as a retry request, so this job retries
/// at most twice (three runs total) with a 15 minute delay each.
/// </summary>
public sealed class ChannelBackfillHangfireJob(IBackfillUseCase backfillUseCase)
{
    [AutomaticRetry(Attempts = 2, DelaysInSeconds = new[] { 900, 900 })]
    public async Task RunPageAsync(
        Guid channelId,
        BackfillTriggerSource triggerSource,
        CancellationToken cancellationToken)
    {
        await backfillUseCase.RunPageAsync(channelId, triggerSource, cancellationToken);
    }
}
