using System.Reflection;
using YtFetcher.Application.Abstractions.Repositories;
using YtFetcher.Application.Abstractions.Services;
using YtFetcher.Application.Abstractions.UseCases;
using YtFetcher.Application.Backfill.DTO.Result;
using YtFetcher.Application.Quotas.DTO.Result;
using YtFetcher.Application.Videos.DTO.Result;
using YtFetcher.Application.Videos.UseCases;
using YtFetcher.Core.BackfillRuns;
using YtFetcher.Core.Channels;
using YtFetcher.Core.Quotas;
using YtFetcher.Core.Videos;

namespace YtFetcher.Api.Tests.UseCases;

/// <summary>
/// The ordered daily phases under the granular bucket model: Phase 0 snapshots the first-page window, M7
/// consumes only Video Batch Stats, M6 consumes Queries and only spends Batch Stats for unknown uploads, and a
/// provider limit completes the date successfully instead of failing the run.
/// </summary>
public class DailyProcessingUseCaseTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 6, 8, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task A_first_run_ensures_the_window_then_polls_monitors_finalizes_and_dispatches()
    {
        var harness = new Harness();
        harness.Statistics.EnqueueBatch(Video("v1"));
        harness.Gateway.Enqueue(VideoStatsBatchResult.Succeeded([Item("v1")]));
        harness.Monitoring.Candidates.Add(MonitorChannel());
        harness.Gateway.UploadPages.Add(UploadsPageResult.Succeeded([PageItem("new-1", Now.AddHours(-1))], null));
        harness.Gateway.Enqueue(VideoStatsBatchResult.Succeeded([new VideoStatsItemResult("new-1", 30_000, 100, 5)]));
        harness.Monitoring.PersistResult = new NewUploadCheckPersistResult(true, true, 1, 0);
        harness.Quota.FinalizationResult = new DailyFinalizationResult(true, true, 9_500, 9_500);
        harness.Backfill.DispatchedChannelIds = [Guid.NewGuid()];

        var result = await harness.UseCase.RunAsync(CancellationToken.None);

        Assert.Equal(DailyProcessingOutcome.Completed, result.Outcome);
        Assert.True(result.GateOpened);
        Assert.Equal(1, result.StatisticsBatchesSucceeded);
        Assert.Equal(1, harness.Quota.EnsureCount);
        Assert.Equal(1, result.UploadVideosInserted);

        AssertOrder(
            harness.Order,
            "ensure",
            "claim",
            "stats-reserve",
            "video-stats-provider",
            "stats-persist",
            "monitor-reserve",
            "monitor-page-provider",
            "stats-reserve",
            "monitor-persist",
            "finalize");
    }

    [Fact]
    public async Task A_second_run_with_a_live_claim_does_no_work()
    {
        var harness = new Harness();
        harness.Quota.ClaimDecision = DailyProcessClaimDecision.AlreadyRunning;

        var result = await harness.UseCase.RunAsync(CancellationToken.None);

        Assert.Equal(DailyProcessingOutcome.AlreadyRunning, result.Outcome);
        Assert.False(result.GateOpened);
        Assert.Equal(0, harness.Quota.FinalizeCount);
        Assert.Empty(harness.Gateway.Requests);
    }

    [Fact]
    public async Task A_completed_date_does_no_work()
    {
        var harness = new Harness();
        harness.Quota.ClaimDecision = DailyProcessClaimDecision.AlreadyCompleted;

        var result = await harness.UseCase.RunAsync(CancellationToken.None);

        Assert.Equal(DailyProcessingOutcome.AlreadyCompleted, result.Outcome);
        Assert.Equal(0, harness.Quota.FinalizeCount);
        Assert.Empty(harness.Gateway.Requests);
    }

    [Fact]
    public async Task A_video_batch_stats_provider_limit_completes_the_date_and_skips_m6()
    {
        var harness = new Harness();
        harness.Statistics.EnqueueBatch(Video("v1"));
        harness.Gateway.Enqueue(VideoStatsBatchResult.QuotaExceeded());
        harness.Monitoring.Candidates.Add(MonitorChannel());
        harness.Quota.FinalizationResult = new DailyFinalizationResult(true, false, 0, 0);

        var result = await harness.UseCase.RunAsync(CancellationToken.None);

        // An expected limit completes the date; it is not a Hangfire failure and it does not spend queries on M6.
        Assert.Equal(DailyProcessingOutcome.Completed, result.Outcome);
        Assert.True(result.VideoBatchStatsQuotaExhausted);
        Assert.Equal(1, harness.Quota.MarkBatchStatsExhaustedCount);
        Assert.Equal(0, harness.Gateway.UploadPageCallCount);
        Assert.Equal(1, harness.Quota.FinalizeCount);
    }

    [Fact]
    public async Task An_unexpected_provider_failure_releases_claims_and_never_finalizes()
    {
        var harness = new Harness();
        harness.Statistics.EnqueueBatch(Video("v1"));
        harness.Gateway.Enqueue(VideoStatsBatchResult.Failed("provider unreachable"));

        await Assert.ThrowsAsync<DailyProcessingFailedException>(() => harness.UseCase.RunAsync(CancellationToken.None));

        Assert.Single(harness.Statistics.ReleasedTokens);
        Assert.Single(harness.Quota.ReleasedDailyClaimTokens);
        Assert.Equal(0, harness.Quota.FinalizeCount);
        Assert.Equal(0, harness.Backfill.DispatchCount);
    }

    [Fact]
    public async Task Losing_statistics_ownership_after_the_provider_call_is_a_no_op_with_a_released_batch()
    {
        var harness = new Harness();
        var video = Video("v1");
        harness.Statistics.EnqueueBatch(video);
        harness.Gateway.Enqueue(VideoStatsBatchResult.Succeeded([Item("v1")]));
        harness.Statistics.OwnershipRetained = false;

        var result = await harness.UseCase.RunAsync(CancellationToken.None);

        // A recovered stale claim is an expected outcome, not a failed daily job.
        Assert.Equal(DailyProcessingOutcome.OwnershipLost, result.Outcome);

        // Only this attempt's own batch is released, and the release is token-matched, so a new owner's claims
        // survive.
        var release = Assert.Single(harness.Statistics.ReleasedClaims);
        Assert.Equal([video.Id], release.VideoIds);
        Assert.NotNull(harness.Statistics.LastClaimToken);
        Assert.Equal(harness.Statistics.LastClaimToken, release.ClaimToken);

        // No later phase ran: no M6 candidate read, finalization, recovery, dispatch, or scheduling, and no
        // quota marker was written for a request this run no longer owns.
        Assert.Equal(0, harness.Monitoring.CandidateReadCount);
        Assert.Equal(0, harness.Quota.FinalizeCount);
        Assert.Equal(0, harness.Quota.MarkBatchStatsExhaustedCount);
        Assert.Equal(0, harness.Quota.MarkQueriesExhaustedCount);
        Assert.Equal(0, harness.Backfill.RecoveryCount);
        Assert.Equal(0, harness.Backfill.DispatchCount);
        Assert.Equal(0, harness.Scheduling.ScheduledCount);
        Assert.Equal(0, harness.Gateway.UploadPageCallCount);
    }

    [Fact]
    public async Task Losing_monitor_ownership_after_the_provider_call_is_a_no_op()
    {
        var harness = new Harness();
        harness.Monitoring.Candidates.Add(MonitorChannel());
        harness.Gateway.UploadPages.Add(UploadsPageResult.Succeeded([PageItem("new-1", Now.AddHours(-1))], null));
        harness.Gateway.Enqueue(VideoStatsBatchResult.Succeeded([new VideoStatsItemResult("new-1", 30_000, 100, 5)]));
        harness.Monitoring.PersistResult = NewUploadCheckPersistResult.OwnershipLost();

        var result = await harness.UseCase.RunAsync(CancellationToken.None);

        Assert.Equal(DailyProcessingOutcome.OwnershipLost, result.Outcome);
        Assert.Equal(0, harness.Quota.FinalizeCount);
        Assert.Equal(0, harness.Quota.MarkBatchStatsExhaustedCount);
        Assert.Equal(0, harness.Quota.MarkQueriesExhaustedCount);
        Assert.Equal(0, harness.Backfill.RecoveryCount);
        Assert.Equal(0, harness.Backfill.DispatchCount);
        Assert.Equal(0, harness.Scheduling.ScheduledCount);
    }

    [Fact]
    public async Task Exhausted_batch_stats_capacity_releases_the_batch_and_completes()
    {
        var harness = new Harness();
        harness.Statistics.EnqueueBatch(Video("v1"));
        harness.Quota.BatchStatsReservation = QuotaReservationResult.NoRemainingCapacity;
        harness.Quota.FinalizationResult = new DailyFinalizationResult(true, false, 0, 0);

        var result = await harness.UseCase.RunAsync(CancellationToken.None);

        Assert.Equal(DailyProcessingOutcome.Completed, result.Outcome);
        Assert.Single(harness.Statistics.ReleasedTokens);
        Assert.Equal(0, harness.Gateway.Requests.Count);
        Assert.Equal(0, harness.Backfill.DispatchCount);
        Assert.True(result.VideoBatchStatsQuotaExhausted);
    }

    [Fact]
    public async Task An_m6_query_limit_completes_the_date_and_marks_the_queries_bucket()
    {
        var harness = new Harness();
        harness.Monitoring.Candidates.Add(MonitorChannel());
        harness.Gateway.UploadPages.Add(UploadsPageResult.QuotaExceeded());
        harness.Quota.FinalizationResult = new DailyFinalizationResult(true, false, 0, 500);

        var result = await harness.UseCase.RunAsync(CancellationToken.None);

        Assert.Equal(DailyProcessingOutcome.Completed, result.Outcome);
        Assert.True(result.QueriesQuotaExhausted);
        Assert.Equal(1, harness.Quota.MarkQueriesExhaustedCount);
        Assert.False(result.VideoBatchStatsQuotaExhausted);
        Assert.Equal(1, harness.Quota.FinalizeCount);
    }

    [Fact]
    public async Task The_dispatch_bound_is_the_live_queries_remainder_within_the_policy()
    {
        var harness = new Harness();
        harness.Quota.FinalizationResult = new DailyFinalizationResult(true, true, 5, 900);
        harness.Backfill.DispatchedChannelIds = [Guid.NewGuid()];

        await harness.UseCase.RunAsync(CancellationToken.None);

        Assert.Equal(5, harness.Backfill.ReceivedDispatchMaximumCount);
    }

    [Fact]
    public async Task A_closed_gate_skips_dispatch_entirely()
    {
        var harness = new Harness();
        harness.Quota.FinalizationResult = new DailyFinalizationResult(true, false, 100, 0);

        var result = await harness.UseCase.RunAsync(CancellationToken.None);

        Assert.False(result.GateOpened);
        Assert.Equal(0, harness.Backfill.DispatchCount);
        Assert.Equal(0, harness.Backfill.RecoveryCount);
    }

    [Fact]
    public void The_use_case_does_not_depend_on_application_settings()
    {
        var parameterTypes = typeof(DailyProcessingUseCase)
            .GetConstructors(BindingFlags.Public | BindingFlags.Instance)
            .SelectMany(constructor => constructor.GetParameters())
            .Select(parameter => parameter.ParameterType)
            .ToArray();

        Assert.DoesNotContain(parameterTypes, type => type.Name == "IAppSettingsService");
        Assert.Contains(typeof(IBackfillDispatchBatchSizePolicy), parameterTypes);
    }

    [Fact]
    public async Task A_stale_daily_owner_is_reported_when_the_m7_batch_stats_reservation_is_fenced()
    {
        var harness = new Harness();
        harness.Statistics.EnqueueBatch(Video("v1"));
        harness.Quota.BatchStatsReservation = QuotaReservationResult.OwnershipLost;

        var result = await harness.UseCase.RunAsync(CancellationToken.None);

        Assert.Equal(DailyProcessingOutcome.OwnershipLost, result.Outcome);
        Assert.Single(harness.Statistics.ReleasedTokens);
        Assert.Equal(0, harness.Quota.FinalizeCount);
        Assert.Equal(0, harness.Backfill.DispatchCount);
        Assert.Empty(harness.Gateway.Requests);
    }

    [Fact]
    public async Task A_stale_daily_owner_is_reported_when_the_m6_queries_reservation_is_fenced()
    {
        var harness = new Harness();
        harness.Monitoring.Candidates.Add(MonitorChannel());
        harness.Quota.QueriesReservation = QuotaReservationResult.OwnershipLost;

        var result = await harness.UseCase.RunAsync(CancellationToken.None);

        Assert.Equal(DailyProcessingOutcome.OwnershipLost, result.Outcome);
        Assert.Equal(0, harness.Gateway.UploadPageCallCount);
        Assert.Equal(0, harness.Quota.FinalizeCount);
        Assert.Equal(0, harness.Backfill.DispatchCount);
    }

    [Fact]
    public async Task Daily_reservations_carry_the_daily_claim_token()
    {
        var harness = new Harness();
        harness.Statistics.EnqueueBatch(Video("v1"));
        harness.Gateway.Enqueue(VideoStatsBatchResult.Succeeded([Item("v1")]));
        harness.Monitoring.Candidates.Add(MonitorChannel());
        harness.Gateway.UploadPages.Add(UploadsPageResult.Succeeded([PageItem("new-1", Now.AddHours(-1))], null));
        harness.Gateway.Enqueue(VideoStatsBatchResult.Succeeded([new VideoStatsItemResult("new-1", 30_000, 100, 5)]));

        await harness.UseCase.RunAsync(CancellationToken.None);

        // M7 and M6 are the only owners that fence their ordinary reservations with the daily token.
        Assert.NotNull(harness.Quota.LastBatchStatsToken);
        Assert.NotNull(harness.Quota.LastQueriesToken);
    }

    [Fact]
    public async Task The_dispatch_bound_is_the_smaller_of_both_live_remainders()
    {
        var harness = new Harness();
        harness.Quota.FinalizationResult = new DailyFinalizationResult(true, true, 10, 1);
        harness.Backfill.DispatchedChannelIds = [Guid.NewGuid()];

        await harness.UseCase.RunAsync(CancellationToken.None);

        Assert.Equal(1, harness.Backfill.ReceivedDispatchMaximumCount);
    }

    [Fact]
    public async Task Recovery_and_fresh_dispatch_stay_within_the_total_bound()
    {
        var harness = new Harness();
        harness.Quota.FinalizationResult = new DailyFinalizationResult(true, true, 10, 10);
        harness.Backfill.RecoveredChannelIds = [Guid.NewGuid(), Guid.NewGuid()];
        harness.Backfill.AbandonedRunCount = 2;
        harness.Backfill.DispatchedChannelIds = [Guid.NewGuid()];

        await harness.UseCase.RunAsync(CancellationToken.None);

        Assert.Equal(10, harness.Backfill.ReceivedRecoveryMaximumCount);
        // 10 total - 2 recovered - 2 abandoned = 6 left for fresh dispatch.
        Assert.Equal(6, harness.Backfill.ReceivedDispatchMaximumCount);
    }

    [Fact]
    public async Task A_zero_remainder_in_either_bucket_dispatches_none()
    {
        var harness = new Harness();
        harness.Quota.FinalizationResult = new DailyFinalizationResult(true, true, 10, 0);

        var result = await harness.UseCase.RunAsync(CancellationToken.None);

        Assert.True(result.GateOpened);
        Assert.Equal(0, harness.Backfill.RecoveryCount);
        Assert.Equal(0, harness.Backfill.DispatchCount);
    }

    private static void AssertOrder(List<string> order, params string[] expectedInOrder)
    {
        var previousIndex = -1;

        foreach (var name in expectedInOrder)
        {
            var index = order.IndexOf(name, previousIndex + 1);
            Assert.True(index > previousIndex, $"'{name}' was not observed after the previous event. Actual order: {string.Join(", ", order)}");
            previousIndex = index;
        }
    }

    private static Video Video(string youtubeVideoId) => new()
    {
        Id = Guid.NewGuid(),
        YoutubeVideoId = youtubeVideoId
    };

    private static VideoStatsItemResult Item(string youtubeVideoId) =>
        new(youtubeVideoId, DurationMillis: 30_000, ViewCount: 1_000, LikeCount: 50);

    private static Channel MonitorChannel() => new()
    {
        Id = Guid.NewGuid(),
        YoutubeChannelId = $"UC-{Guid.NewGuid():N}",
        Name = "Monitored",
        Status = ChannelStatus.APPROVED,
        BackfillStatus = BackfillStatus.COMPLETED,
        UploadsPlaylistId = "UU-monitored",
        IsDeleted = false,
        ApprovedAt = Now,
        DiscoveredAt = Now,
        CreatedAt = Now,
        ModifiedAt = Now
    };

    private static UploadsPageItemResult PageItem(string youtubeVideoId, DateTimeOffset publishedAt) =>
        new(youtubeVideoId, $"Title {youtubeVideoId}", null, publishedAt);

    private sealed class Harness
    {
        public Harness()
        {
            Quota = new FakeDailyQuotaRepository(Order);
            Statistics = new FakeVideoStatisticsRepository(Order);
            Gateway = new FakeYouTubeBackfillGateway(Order);
            Monitoring = new FakeNewUploadMonitoringRepository(Order);

            UseCase = new DailyProcessingUseCase(
                Quota,
                Statistics,
                Gateway,
                Monitoring,
                new FakeMonitorLogger(),
                Backfill,
                new FakePageLimitPolicy(),
                new FakeDispatchBatchSizePolicy(),
                Scheduling,
                new FixedTimeProvider(Now));
        }

        public List<string> Order { get; } = [];

        public FakeDailyQuotaRepository Quota { get; }

        public FakeVideoStatisticsRepository Statistics { get; }

        public FakeYouTubeBackfillGateway Gateway { get; }

        public FakeNewUploadMonitoringRepository Monitoring { get; }

        public FakeBackfillRepository Backfill { get; } = new();

        public RecordingSchedulingUseCase Scheduling { get; } = new();

        public IRunDailyProcessingUseCase UseCase { get; }
    }

    private sealed class FixedTimeProvider(DateTimeOffset utcNow) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => utcNow;
    }

    private sealed class FakeDailyQuotaRepository(List<string> order) : IDailyQuotaRepository
    {
        public DailyProcessClaimDecision ClaimDecision { get; set; } = DailyProcessClaimDecision.Claimed;

        public QuotaReservationResult BatchStatsReservation { get; set; } = QuotaReservationResult.Reserved;

        public QuotaReservationResult QueriesReservation { get; set; } = QuotaReservationResult.Reserved;

        public DailyFinalizationResult FinalizationResult { get; set; } = new(true, true, 9_000, 9_000);

        public int EnsureCount { get; private set; }

        public int FinalizeCount { get; private set; }

        public int MarkBatchStatsExhaustedCount { get; private set; }

        public int MarkQueriesExhaustedCount { get; private set; }

        public List<Guid> ReleasedDailyClaimTokens { get; } = [];

        public Guid? LastQueriesToken { get; private set; }

        public Guid? LastBatchStatsToken { get; private set; }

        public Task<YouTubeDailyQuotaUsage?> GetForDateAsync(DateOnly quotaDate, CancellationToken cancellationToken) =>
            Task.FromResult<YouTubeDailyQuotaUsage?>(new YouTubeDailyQuotaUsage
            {
                FirstPageAttemptLimit = 500,
                FirstPageAttemptUsed = 0,
                QueriesUnitsUsed = 300,
                VideoBatchStatsUnitsUsed = 400
            });

        public Task EnsureLedgerAsync(DateOnly quotaDate, CancellationToken cancellationToken)
        {
            EnsureCount++;
            order.Add("ensure");
            return Task.CompletedTask;
        }

        public Task<DailyProcessClaimDecision> TryClaimDailyProcessAsync(DateOnly quotaDate, Guid claimToken, DateTimeOffset utcNow, CancellationToken cancellationToken)
        {
            order.Add("claim");
            return Task.FromResult(ClaimDecision);
        }

        public Task ReleaseDailyProcessClaimAsync(DateOnly quotaDate, Guid claimToken, CancellationToken cancellationToken)
        {
            ReleasedDailyClaimTokens.Add(claimToken);
            return Task.CompletedTask;
        }

        public Task<DailyFinalizationResult> FinalizeDailyProcessingAsync(DateOnly quotaDate, Guid claimToken, DateTimeOffset utcNow, CancellationToken cancellationToken)
        {
            FinalizeCount++;
            order.Add("finalize");
            return Task.FromResult(FinalizationResult);
        }

        public Task<QuotaReservationResult> TryReserveQueriesUnitAsync(DateOnly quotaDate, Guid? dailyProcessClaimToken, CancellationToken cancellationToken)
        {
            LastQueriesToken = dailyProcessClaimToken;
            order.Add("monitor-reserve");
            return Task.FromResult(QueriesReservation);
        }

        public Task<QuotaReservationResult> TryReserveVideoBatchStatsUnitAsync(DateOnly quotaDate, Guid? dailyProcessClaimToken, CancellationToken cancellationToken)
        {
            LastBatchStatsToken = dailyProcessClaimToken;
            order.Add("stats-reserve");
            return Task.FromResult(BatchStatsReservation);
        }

        public Task<QuotaReservationResult> TryReserveSearchUnitAsync(DateOnly quotaDate, Guid? dailyProcessClaimToken, CancellationToken cancellationToken) =>
            Task.FromResult(QuotaReservationResult.Reserved);

        public Task MarkSearchQuotaExhaustedAsync(DateOnly quotaDate, DateTimeOffset utcNow, CancellationToken cancellationToken) => Task.CompletedTask;

        public Task MarkQueriesQuotaExhaustedAsync(DateOnly quotaDate, DateTimeOffset utcNow, CancellationToken cancellationToken)
        {
            MarkQueriesExhaustedCount++;
            return Task.CompletedTask;
        }

        public Task MarkVideoBatchStatsQuotaExhaustedAsync(DateOnly quotaDate, DateTimeOffset utcNow, CancellationToken cancellationToken)
        {
            MarkBatchStatsExhaustedCount++;
            return Task.CompletedTask;
        }
    }

    private sealed class FakeVideoStatisticsRepository(List<string> order) : IVideoStatisticsRepository
    {
        private readonly Queue<IReadOnlyList<Video>> batches = new();

        public bool OwnershipRetained { get; set; } = true;

        public List<Guid> ReleasedTokens { get; } = [];

        public List<(IReadOnlyList<Guid> VideoIds, Guid ClaimToken)> ReleasedClaims { get; } = [];

        public Guid? LastClaimToken { get; private set; }

        public void EnqueueBatch(params Video[] videos) => batches.Enqueue(videos);

        public Task<int> ReleaseExpiredClaimsAsync(DateTimeOffset claimedBefore, DateTimeOffset utcNow, CancellationToken cancellationToken) =>
            Task.FromResult(0);

        public Task<IReadOnlyList<Video>> ClaimDueBatchAsync(DateTimeOffset quotaDateStartUtc, DateTimeOffset utcNow, Guid claimToken, int batchSize, CancellationToken cancellationToken)
        {
            LastClaimToken = claimToken;
            return Task.FromResult(batches.Count > 0 ? batches.Dequeue() : (IReadOnlyList<Video>)Array.Empty<Video>());
        }

        public Task<VideoStatisticsPersistResult> PersistSuccessAsync(DateOnly quotaDate, Guid dailyProcessClaimToken, IReadOnlyList<Guid> claimedVideoIds, Guid claimToken, IReadOnlyList<VideoStatsItemResult> items, DateTimeOffset fetchedAt, CancellationToken cancellationToken)
        {
            order.Add("stats-persist");
            return Task.FromResult(OwnershipRetained
                ? new VideoStatisticsPersistResult(true, items.Count, claimedVideoIds.Count - items.Count)
                : VideoStatisticsPersistResult.OwnershipLost());
        }

        public Task ReleaseClaimsAsync(IReadOnlyList<Guid> claimedVideoIds, Guid claimToken, DateTimeOffset utcNow, CancellationToken cancellationToken)
        {
            ReleasedTokens.Add(claimToken);
            ReleasedClaims.Add((claimedVideoIds.ToArray(), claimToken));
            return Task.CompletedTask;
        }
    }

    private sealed class FakeYouTubeBackfillGateway(List<string> order) : IYouTubeBackfillGateway
    {
        private readonly List<VideoStatsBatchResult> results = [];
        private int next;

        public List<IReadOnlyList<string>> Requests { get; } = [];

        public List<UploadsPageResult> UploadPages { get; } = [];

        public int UploadPageCallCount { get; private set; }

        public void Enqueue(VideoStatsBatchResult result) => results.Add(result);

        public Task<VideoStatsBatchResult> GetVideoStatsAsync(IReadOnlyCollection<string> youtubeVideoIds, CancellationToken cancellationToken)
        {
            Requests.Add(youtubeVideoIds.ToArray());
            order.Add("video-stats-provider");
            var result = next < results.Count ? results[next++] : results[^1];
            return Task.FromResult(result);
        }

        public Task<UploadsPageResult> GetUploadsPageAsync(string uploadsPlaylistId, string? pageToken, CancellationToken cancellationToken)
        {
            UploadPageCallCount++;
            order.Add("monitor-page-provider");
            var result = UploadPages.Count == 0
                ? UploadsPageResult.Succeeded([], null)
                : UploadPages[Math.Min(UploadPageCallCount - 1, UploadPages.Count - 1)];
            return Task.FromResult(result);
        }

        public Task<UploadsPlaylistResult> ResolveUploadsPlaylistAsync(string youtubeChannelId, CancellationToken cancellationToken) =>
            Task.FromResult(UploadsPlaylistResult.Succeeded("UU-monitored"));
    }

    private sealed class FakeNewUploadMonitoringRepository(List<string> order) : INewUploadMonitoringRepository
    {
        public List<Channel> Candidates { get; } = [];

        public HashSet<string> KnownIds { get; } = new(StringComparer.Ordinal);

        public NewUploadCheckPersistResult PersistResult { get; set; } = new(true, true, 0, 0);

        public int CandidateReadCount { get; private set; }

        public Task<IReadOnlyList<Channel>> GetUploadMonitorCandidatesAsync(DateTimeOffset quotaDateStartUtc, CancellationToken cancellationToken)
        {
            CandidateReadCount++;
            return Task.FromResult<IReadOnlyList<Channel>>(Candidates);
        }

        public Task<IReadOnlySet<string>> GetKnownYoutubeVideoIdsAsync(IReadOnlyCollection<string> youtubeVideoIds, CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlySet<string>>(youtubeVideoIds.Where(KnownIds.Contains).ToHashSet(StringComparer.Ordinal));

        public Task<NewUploadCheckPersistResult> PersistUploadCheckAsync(NewUploadCheckRequest request, CancellationToken cancellationToken)
        {
            order.Add("monitor-persist");
            return Task.FromResult(PersistResult);
        }
    }

    private sealed class FakeMonitorLogger : INewUploadMonitorActivityLogger
    {
        public void LogProviderFailed(Guid channelId, string operation, string reason) { }
        public void LogPersistenceFailed(Guid channelId, string operation, Exception exception) { }
    }

    private sealed class FakeBackfillRepository : IBackfillRepository
    {
        public IReadOnlyList<Guid> RecoveredChannelIds { get; set; } = [];

        public IReadOnlyList<Guid> DispatchedChannelIds { get; set; } = [];

        public int AbandonedRunCount { get; set; }

        public int RecoveryCount { get; private set; }

        public int DispatchCount { get; private set; }

        public int ReceivedRecoveryMaximumCount { get; private set; } = -1;

        public int ReceivedDispatchMaximumCount { get; private set; } = -1;

        public Task<BackfillRecoveryResult> RecoverStaleWorkAsync(DateTimeOffset staleBeforeUtc, DateTimeOffset utcNow, int maximumCount, CancellationToken cancellationToken)
        {
            RecoveryCount++;
            ReceivedRecoveryMaximumCount = maximumCount;
            return Task.FromResult(new BackfillRecoveryResult(RecoveredChannelIds, AbandonedRunCount));
        }

        public Task<BackfillDispatchResult> DispatchCandidatesAsync(int pageLimit, DateTimeOffset utcNow, int maximumCount, CancellationToken cancellationToken)
        {
            DispatchCount++;
            ReceivedDispatchMaximumCount = maximumCount;
            return Task.FromResult(new BackfillDispatchResult(DispatchedChannelIds));
        }

        public Task<Channel?> TryClaimPageAsync(Guid channelId, Guid claimToken, DateOnly quotaDate, BackfillTriggerSource triggerSource, DateTimeOffset utcNow, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task ReleaseClaimAsync(Guid channelId, Guid claimToken, DateTimeOffset utcNow, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<BackfillPagePersistResult> PersistPageAsync(BackfillPageResult page, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<bool> DeferForQuotaLimitAsync(Guid channelId, Guid claimToken, QuotaLimitBucket bucket, DateTimeOffset utcNow, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<BackfillFailureResult?> RecordFailureAsync(Guid channelId, Guid claimToken, string failureReason, DateTimeOffset utcNow, CancellationToken cancellationToken) => throw new NotSupportedException();
    }

    private sealed class FakePageLimitPolicy : IBackfillPageLimitPolicy
    {
        public int PageLimit => 3;
    }

    private sealed class FakeDispatchBatchSizePolicy : IBackfillDispatchBatchSizePolicy
    {
        public int BatchSize => 10;
    }

    private sealed class RecordingSchedulingUseCase : IBackfillSchedulingUseCase
    {
        public int ScheduledCount { get; private set; }

        public Task ScheduleAsync(Guid channelId, BackfillTriggerSource triggerSource, CancellationToken cancellationToken)
        {
            ScheduledCount++;
            return Task.CompletedTask;
        }
    }
}
