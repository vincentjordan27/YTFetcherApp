namespace YtFetcher.Application.Videos.DTO.Result;

/// <summary>
/// Scalar outcome of one daily processing trigger. It carries safe counts only, which is what the Hangfire
/// adapter needs for one structured completion event.
/// </summary>
public sealed record DailyProcessingResult(
    DateOnly QuotaDate,
    DailyProcessingOutcome Outcome,
    int StatisticsBatchesSucceeded,
    int VideosUpdated,
    int VideosUnavailable,
    int UploadChannelsChecked,
    int UploadChannelsWithNewVideos,
    int UploadVideosInserted,
    int UploadKnownVideosSkipped,
    int UploadOtherVideosSkipped,
    int QueriesUnitsUsed,
    int VideoBatchStatsUnitsUsed,
    int SearchQueryUnitsUsed,
    bool QueriesQuotaExhausted,
    bool VideoBatchStatsQuotaExhausted,
    bool GateOpened,
    int RecoveredChannels,
    int AbandonedRuns,
    int ChannelsDispatched)
{
    /// <summary>A trigger that correctly did nothing: no provider call, no completion write, no dispatch.</summary>
    public static DailyProcessingResult NoOp(DateOnly quotaDate, DailyProcessingOutcome outcome) =>
        new(quotaDate, outcome, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, false, false, false, 0, 0, 0);
}
