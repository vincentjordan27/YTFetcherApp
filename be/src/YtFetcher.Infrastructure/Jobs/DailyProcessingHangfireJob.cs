using Hangfire;
using Microsoft.Extensions.Logging;
using YtFetcher.Application.Abstractions.UseCases;
using YtFetcher.Application.Videos.UseCases;

namespace YtFetcher.Infrastructure.Jobs;

/// <summary>
/// Recurring Hangfire adapter for daily processing. It calls one Application use case once and holds no
/// scheduling rule. Automatic retry is disabled deliberately: a whole daily run must never be silently
/// replayed after partial success. An expected provider quota limit completes the date successfully; only an
/// unexpected phase failure is rethrown so the dashboard marks the run failed.
/// </summary>
public sealed class DailyProcessingHangfireJob(
    IRunDailyProcessingUseCase runDailyProcessingUseCase,
    ILogger<DailyProcessingHangfireJob> logger)
{
    [AutomaticRetry(Attempts = 0)]
    public async Task RunAsync(CancellationToken cancellationToken)
    {
        try
        {
            var result = await runDailyProcessingUseCase.RunAsync(cancellationToken);

            // Scalars only: the same counts the use case returns, never provider bodies or connection details.
            logger.LogInformation(
                "DailyProcessingCompleted {QuotaDate} {Outcome} {BatchesSucceeded} {VideosUpdated} " +
                "{VideosUnavailable} {UploadChannelsChecked} {UploadChannelsWithNewVideos} {UploadVideosInserted} " +
                "{UploadKnownVideosSkipped} {UploadOtherVideosSkipped} {QueriesUnitsUsed} " +
                "{VideoBatchStatsUnitsUsed} {SearchQueryUnitsUsed} {QueriesQuotaExhausted} " +
                "{VideoBatchStatsQuotaExhausted} {GateOpened} {RecoveredChannels} {AbandonedRuns} " +
                "{ChannelsDispatched}",
                result.QuotaDate,
                result.Outcome,
                result.StatisticsBatchesSucceeded,
                result.VideosUpdated,
                result.VideosUnavailable,
                result.UploadChannelsChecked,
                result.UploadChannelsWithNewVideos,
                result.UploadVideosInserted,
                result.UploadKnownVideosSkipped,
                result.UploadOtherVideosSkipped,
                result.QueriesUnitsUsed,
                result.VideoBatchStatsUnitsUsed,
                result.SearchQueryUnitsUsed,
                result.QueriesQuotaExhausted,
                result.VideoBatchStatsQuotaExhausted,
                result.GateOpened,
                result.RecoveredChannels,
                result.AbandonedRuns,
                result.ChannelsDispatched);

            if (result.QueriesQuotaExhausted || result.VideoBatchStatsQuotaExhausted)
            {
                logger.LogWarning(
                    "DailyProcessingQuotaLimited {QuotaDate} {QueriesQuotaExhausted} {VideoBatchStatsQuotaExhausted}",
                    result.QuotaDate,
                    result.QueriesQuotaExhausted,
                    result.VideoBatchStatsQuotaExhausted);
            }
        }
        catch (DailyProcessingFailedException failure)
        {
            // One safe structured batch failure, then rethrow so Hangfire marks the run failed instead of the
            // run continuing into completion, gate opening, or backfill dispatch.
            logger.LogWarning(failure, "DailyProcessingFailed {FailureReason}", failure.FailureReason);
            throw;
        }
    }
}
