using YtFetcher.Core.BackfillRuns;

namespace YtFetcher.Application.Abstractions.Services;

/// <summary>
/// Scheduling boundary for approved-channel backfill work. The adapter enqueues exactly one generic
/// page job and contains no backfill rules.
/// </summary>
public interface IBackfillScheduler
{
    /// <summary>
    /// Enqueues exactly one page job for the channel. <paramref name="triggerSource"/> travels with the
    /// job so the durable run history records why this attempt was dispatched.
    /// </summary>
    void EnqueuePage(Guid channelId, BackfillTriggerSource triggerSource);
}
