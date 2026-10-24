using Microsoft.Extensions.Logging;
using YtFetcher.Api.Tests.Channels;
using YtFetcher.Application.Abstractions.UseCases;
using YtFetcher.Application.Videos.DTO.Result;
using YtFetcher.Application.Videos.UseCases;
using YtFetcher.Infrastructure.Jobs;

namespace YtFetcher.Api.Tests.Backfill;

/// <summary>
/// The daily job adapter owns the one aggregate operator event and the controlled failure. It is asserted with a
/// recording logger; no Hangfire server, storage, or scheduler is involved.
/// </summary>
public class DailyProcessingHangfireJobTests
{
    private static readonly DateOnly QuotaDate = new(2026, 10, 6);

    [Fact]
    public async Task One_invocation_emits_exactly_one_daily_completed_information_event()
    {
        var logSink = new TestLogSink();
        var result = new DailyProcessingResult(
            QuotaDate,
            DailyProcessingOutcome.Completed,
            StatisticsBatchesSucceeded: 2,
            VideosUpdated: 7,
            VideosUnavailable: 1,
            RecoveredChannels: 3,
            AbandonedRuns: 1,
            ChannelsDispatched: 5,
            GateOpened: true,
            UploadChannelsChecked: 4,
            UploadChannelsWithNewVideos: 2,
            UploadVideosInserted: 6,
            UploadKnownVideosSkipped: 9,
            UploadOtherVideosSkipped: 1,
            QueriesUnitsUsed: 8,
            VideoBatchStatsUnitsUsed: 2,
            SearchQueryUnitsUsed: 1,
            QueriesQuotaExhausted: false,
            VideoBatchStatsQuotaExhausted: false);

        var job = new DailyProcessingHangfireJob(new StubUseCase(result), logSink.CreateLogger<DailyProcessingHangfireJob>());

        await job.RunAsync(CancellationToken.None);

        var entry = Assert.Single(logSink.Entries);
        Assert.Equal(LogLevel.Information, entry.Level);
        Assert.StartsWith("DailyProcessingCompleted", entry.Message, StringComparison.Ordinal);

        // One aggregate summary: the scalar outcome and the counts, and no provider payload.
        Assert.Equal(QuotaDate, entry.Properties["QuotaDate"]);
        Assert.Equal(2, entry.Properties["BatchesSucceeded"]);
        Assert.Equal(7, entry.Properties["VideosUpdated"]);
        Assert.Equal(1, entry.Properties["VideosUnavailable"]);
        Assert.Equal(3, entry.Properties["RecoveredChannels"]);
        Assert.Equal(1, entry.Properties["AbandonedRuns"]);
        Assert.Equal(5, entry.Properties["ChannelsDispatched"]);
        Assert.Equal(true, entry.Properties["GateOpened"]);
        Assert.Equal(4, entry.Properties["UploadChannelsChecked"]);
        Assert.Equal(2, entry.Properties["UploadChannelsWithNewVideos"]);
        Assert.Equal(6, entry.Properties["UploadVideosInserted"]);
        Assert.Equal(9, entry.Properties["UploadKnownVideosSkipped"]);
        Assert.Equal(1, entry.Properties["UploadOtherVideosSkipped"]);
        Assert.Equal(8, entry.Properties["QueriesUnitsUsed"]);
        Assert.Equal(2, entry.Properties["VideoBatchStatsUnitsUsed"]);
        Assert.Equal(1, entry.Properties["SearchQueryUnitsUsed"]);
        Assert.Equal(false, entry.Properties["QueriesQuotaExhausted"]);
        Assert.Equal(false, entry.Properties["VideoBatchStatsQuotaExhausted"]);
    }

    [Fact]
    public async Task A_controlled_daily_failure_is_logged_once_at_warning_and_rethrown()
    {
        var logSink = new TestLogSink();
        var job = new DailyProcessingHangfireJob(
            new StubUseCase(null),
            logSink.CreateLogger<DailyProcessingHangfireJob>());

        var exception = await Assert.ThrowsAsync<DailyProcessingFailedException>(
            () => job.RunAsync(CancellationToken.None));

        Assert.Equal("provider failure", exception.FailureReason);

        var entry = Assert.Single(logSink.Entries);
        Assert.Equal(LogLevel.Warning, entry.Level);
        Assert.StartsWith("DailyProcessingFailed", entry.Message, StringComparison.Ordinal);
        Assert.Equal("provider failure", entry.Properties["FailureReason"]);
    }

    /// <summary>Returns one scripted result, or throws the safe daily failure when no result is supplied.</summary>
    private sealed class StubUseCase(DailyProcessingResult? result) : IRunDailyProcessingUseCase
    {
        public Task<DailyProcessingResult> RunAsync(CancellationToken cancellationToken) =>
            result is null
                ? Task.FromException<DailyProcessingResult>(new DailyProcessingFailedException("provider failure"))
                : Task.FromResult(result);
    }
}
