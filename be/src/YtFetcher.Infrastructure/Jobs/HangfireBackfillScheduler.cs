using Hangfire;
using YtFetcher.Application.Abstractions.Services;
using YtFetcher.Core.BackfillRuns;

namespace YtFetcher.Infrastructure.Jobs;

/// <summary>
/// Hangfire adapter for the scheduling port. It contains no backfill rules: it enqueues exactly one
/// generic page job for the channel and passes the trigger source through unchanged.
/// </summary>
public sealed class HangfireBackfillScheduler(IBackgroundJobClient backgroundJobClient) : IBackfillScheduler
{
    public void EnqueuePage(Guid channelId, BackfillTriggerSource triggerSource) =>
        backgroundJobClient.Enqueue<ChannelBackfillHangfireJob>(
            job => job.RunPageAsync(channelId, triggerSource, CancellationToken.None));
}
