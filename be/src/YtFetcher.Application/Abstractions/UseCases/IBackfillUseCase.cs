using YtFetcher.Core.BackfillRuns;

namespace YtFetcher.Application.Abstractions.UseCases;

/// <summary>
/// Application use-case boundary for the approved-channel backfill. One call processes exactly one
/// uploads playlist page, whether that page is the first one, the next continuation, or the retry of a
/// failed page, and it decides the page from the durable state of the claimed channel. Expected provider
/// and quota outcomes are handled inside and return normally.
/// </summary>
public interface IBackfillUseCase
{
    Task RunPageAsync(
        Guid channelId,
        BackfillTriggerSource triggerSource,
        CancellationToken cancellationToken);
}
