using YtFetcher.Application.Abstractions.Repositories;
using YtFetcher.Application.Abstractions.Services;
using YtFetcher.Application.Backfill;
using YtFetcher.Application.Backfill.DTO.Result;
using YtFetcher.Application.Backfill.UseCases;
using YtFetcher.Application.Quotas.DTO.Result;
using YtFetcher.Core.BackfillRuns;
using YtFetcher.Core.Channels;
using YtFetcher.Core.Quotas;

namespace YtFetcher.Api.Tests.UseCases;

/// <summary>
/// M5 page orchestration under the granular bucket model: the Queries reservation must precede the playlist
/// call, the Video Batch Stats reservation must follow a non-empty playlist, page one must not reserve either
/// bucket again, and a refusal or provider limit must never call the next provider endpoint.
/// </summary>
public class BackfillUseCaseTests
{
    // 05:00 UTC is 22:00 on the previous day in Pacific time, so the quota date must be 2026-09-30.
    private static readonly DateTimeOffset Now = new(2026, 10, 1, 5, 0, 0, TimeSpan.Zero);
    private static readonly DateOnly QuotaDate = new(2026, 9, 30);

    private const string UploadsPlaylistId = "UU-channel";

    [Fact]
    public async Task An_unclaimable_channel_skips_every_provider_call_and_reservation()
    {
        var (useCase, repository, gateway, quota, _) = CreateUseCase(claimedChannel: null);

        await useCase.RunPageAsync(Guid.NewGuid(), BackfillTriggerSource.APPROVAL, CancellationToken.None);

        Assert.Equal(1, repository.ClaimCount);
        Assert.Equal(0, gateway.PageCallCount);
        Assert.Equal(0, gateway.StatsCallCount);
        Assert.Equal(0, quota.QueriesReservations);
        Assert.Equal(0, quota.BatchStatsReservations);
        Assert.Equal(0, repository.PersistCount);
    }

    [Fact]
    public async Task A_missing_uploads_playlist_is_a_readable_failure_with_no_provider_call()
    {
        var channel = ClaimedChannel(uploadsPlaylistId: null);
        var (useCase, repository, gateway, quota, _) = CreateUseCase(channel);

        await useCase.RunPageAsync(channel.Id, BackfillTriggerSource.APPROVAL, CancellationToken.None);

        Assert.Equal(0, gateway.PageCallCount);
        Assert.Equal(0, quota.QueriesReservations);
        Assert.Equal(1, repository.FailureCount);
        Assert.Contains("uploads playlist", Assert.Single(repository.Failures).Reason, StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_freshly_reserved_page_one_that_hits_a_provider_limit_marks_the_bucket_and_defers()
    {
        // The repository already reclaimed today's paired reservation inside the claim transaction, so the worker
        // reserves nothing again and a provider limit closes the run as QUOTA_LIMIT_REACHED with the bucket.
        var channel = ClaimedChannel(firstPageQuotaDate: QuotaDate);
        var (useCase, repository, gateway, quota, _) = CreateUseCase(
            channel,
            page: UploadsPageResult.QuotaExceeded());

        await useCase.RunPageAsync(channel.Id, BackfillTriggerSource.APPROVAL, CancellationToken.None);

        Assert.Equal(0, quota.QueriesReservations);
        Assert.Equal(0, quota.BatchStatsReservations);
        Assert.Equal(1, quota.QueriesExhaustedMarks);
        Assert.Equal([QuotaLimitBucket.QUERIES], repository.DeferredForQuota.Select(entry => entry.Bucket));
        Assert.Equal(0, repository.FailureCount);
    }

    [Fact]
    public async Task A_page_one_does_not_reserve_either_bucket_again()
    {
        var channel = ClaimedChannel();
        var (useCase, repository, gateway, quota, _) = CreateUseCase(
            channel,
            page: UploadsPageResult.Succeeded([UploadItem("v1")], nextPageToken: null),
            stats: VideoStatsBatchResult.Succeeded([StatsItem("v1")]));

        await useCase.RunPageAsync(channel.Id, BackfillTriggerSource.APPROVAL, CancellationToken.None);

        Assert.Equal(0, quota.QueriesReservations);
        Assert.Equal(0, quota.BatchStatsReservations);
        Assert.Equal(1, gateway.PageCallCount);
        Assert.Equal(1, gateway.StatsCallCount);
        Assert.Equal(1, repository.PersistCount);
    }

    [Fact]
    public async Task A_continuation_reserves_queries_before_the_playlist_and_batch_stats_before_stats()
    {
        var channel = ClaimedChannel(pagesCompleted: 1, pageToken: "page-2");
        var (useCase, repository, gateway, quota, _) = CreateUseCase(
            channel,
            page: UploadsPageResult.Succeeded([UploadItem("v1")], nextPageToken: null),
            stats: VideoStatsBatchResult.Succeeded([StatsItem("v1")]));

        await useCase.RunPageAsync(channel.Id, BackfillTriggerSource.DAILY_PUMP, CancellationToken.None);

        Assert.Equal(1, quota.QueriesReservations);
        Assert.Equal(1, quota.BatchStatsReservations);
        Assert.Equal(1, repository.PersistCount);
    }

    [Fact]
    public async Task An_empty_continuation_consumes_only_one_queries_unit_and_never_calls_stats()
    {
        var channel = ClaimedChannel(pagesCompleted: 1, pageToken: "page-2");
        var (useCase, repository, gateway, quota, _) = CreateUseCase(
            channel,
            page: UploadsPageResult.Succeeded([], nextPageToken: null));

        await useCase.RunPageAsync(channel.Id, BackfillTriggerSource.DAILY_PUMP, CancellationToken.None);

        Assert.Equal(1, quota.QueriesReservations);
        Assert.Equal(0, quota.BatchStatsReservations);
        Assert.Equal(0, gateway.StatsCallCount);
        Assert.Equal(1, repository.PersistCount);
    }

    [Fact]
    public async Task A_refused_queries_reservation_defers_the_run_without_any_provider_call()
    {
        var channel = ClaimedChannel(pagesCompleted: 1, pageToken: "page-2");
        var (useCase, repository, gateway, quota, _) = CreateUseCase(
            channel,
            page: UploadsPageResult.Succeeded([UploadItem("v1")], nextPageToken: null),
            queriesReservation: QuotaReservationResult.NoRemainingCapacity);

        await useCase.RunPageAsync(channel.Id, BackfillTriggerSource.DAILY_PUMP, CancellationToken.None);

        Assert.Equal(0, gateway.PageCallCount);
        Assert.Equal(0, repository.PersistCount);
        Assert.Equal([QuotaLimitBucket.QUERIES], repository.DeferredForQuota.Select(entry => entry.Bucket));
    }

    [Fact]
    public async Task A_refused_batch_stats_reservation_defers_without_calling_stats()
    {
        var channel = ClaimedChannel(pagesCompleted: 1, pageToken: "page-2");
        var (useCase, repository, gateway, quota, _) = CreateUseCase(
            channel,
            page: UploadsPageResult.Succeeded([UploadItem("v1")], nextPageToken: "page-3"),
            batchStatsReservation: QuotaReservationResult.NoRemainingCapacity);

        await useCase.RunPageAsync(channel.Id, BackfillTriggerSource.DAILY_PUMP, CancellationToken.None);

        Assert.Equal(1, gateway.PageCallCount);
        Assert.Equal(0, gateway.StatsCallCount);
        Assert.Equal(0, repository.PersistCount);
        Assert.Equal([QuotaLimitBucket.VIDEO_BATCH_STATS], repository.DeferredForQuota.Select(entry => entry.Bucket));
    }

    [Fact]
    public async Task A_provider_quota_limit_on_the_playlist_marks_queries_and_defers()
    {
        var channel = ClaimedChannel(pagesCompleted: 1, pageToken: "page-2");
        var (useCase, repository, gateway, quota, _) = CreateUseCase(
            channel,
            page: UploadsPageResult.QuotaExceeded());

        await useCase.RunPageAsync(channel.Id, BackfillTriggerSource.DAILY_PUMP, CancellationToken.None);

        Assert.Equal(1, quota.QueriesExhaustedMarks);
        Assert.Equal(0, quota.BatchStatsExhaustedMarks);
        Assert.Equal([QuotaLimitBucket.QUERIES], repository.DeferredForQuota.Select(entry => entry.Bucket));
        Assert.Equal(0, repository.FailureCount);
    }

    [Fact]
    public async Task A_provider_quota_limit_on_stats_marks_batch_stats_and_defers()
    {
        var channel = ClaimedChannel(pagesCompleted: 1, pageToken: "page-2");
        var (useCase, repository, gateway, quota, _) = CreateUseCase(
            channel,
            page: UploadsPageResult.Succeeded([UploadItem("v1")], nextPageToken: null),
            stats: VideoStatsBatchResult.QuotaExceeded());

        await useCase.RunPageAsync(channel.Id, BackfillTriggerSource.DAILY_PUMP, CancellationToken.None);

        Assert.Equal(1, quota.BatchStatsExhaustedMarks);
        Assert.Equal(0, quota.QueriesExhaustedMarks);
        Assert.Equal([QuotaLimitBucket.VIDEO_BATCH_STATS], repository.DeferredForQuota.Select(entry => entry.Bucket));
        Assert.Equal(0, repository.FailureCount);
    }

    [Fact]
    public async Task A_non_quota_provider_failure_records_a_safe_failure_and_retry()
    {
        var channel = ClaimedChannel(pagesCompleted: 1, pageToken: "page-2");
        var (useCase, repository, gateway, _, _) = CreateUseCase(
            channel,
            page: UploadsPageResult.Failed("provider unreachable"),
            reachedFailureLimit: false);

        await Assert.ThrowsAsync<BackfillRetryRequestedException>(
            () => useCase.RunPageAsync(channel.Id, BackfillTriggerSource.DAILY_PUMP, CancellationToken.None));

        Assert.Equal(0, gateway.StatsCallCount);
        Assert.Equal(1, repository.FailureCount);
        Assert.Contains("retrieve uploads", Assert.Single(repository.Failures).Reason, StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_recorded_failure_does_not_request_a_hangfire_retry_when_the_channel_is_no_longer_eligible()
    {
        var channel = ClaimedChannel(pagesCompleted: 1, pageToken: "page-2");
        var (useCase, repository, gateway, _, _) = CreateUseCase(
            channel,
            page: UploadsPageResult.Failed("provider unreachable"),
            reachedFailureLimit: false,
            shouldRequestRetry: false);

        await useCase.RunPageAsync(channel.Id, BackfillTriggerSource.DAILY_PUMP, CancellationToken.None);

        Assert.Equal(0, gateway.StatsCallCount);
        Assert.Equal(1, repository.FailureCount);
        Assert.Contains("retrieve uploads", Assert.Single(repository.Failures).Reason, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Non_short_and_incomplete_statistics_rows_are_not_persisted()
    {
        var channel = ClaimedChannel(pagesCompleted: 1, pageToken: "page-2");
        var (useCase, repository, gateway, _, _) = CreateUseCase(
            channel,
            page: UploadsPageResult.Succeeded([UploadItem("long-1"), UploadItem("missing-1")], nextPageToken: null),
            stats: VideoStatsBatchResult.Succeeded([StatsItem("long-1", durationMillis: 240_000)]));

        await useCase.RunPageAsync(channel.Id, BackfillTriggerSource.DAILY_PUMP, CancellationToken.None);

        // The page carries both upload items; only the rows YouTube described completely reach persistence, and
        // the repository applies the short-form rule to the long row.
        Assert.NotNull(repository.PersistedPage);
        Assert.Equal(2, repository.PersistedPage!.UploadItems.Count);
        Assert.Single(repository.PersistedPage.StatsItems);
    }

    private static (BackfillUseCase UseCase, FakeBackfillRepository Repository, FakeYouTubeBackfillGateway Gateway, FakeDailyQuotaRepository Quota, FakeActivityLogger Logger) CreateUseCase(
        Channel? claimedChannel,
        UploadsPageResult? page = null,
        VideoStatsBatchResult? stats = null,
        QuotaReservationResult queriesReservation = QuotaReservationResult.Reserved,
        QuotaReservationResult batchStatsReservation = QuotaReservationResult.Reserved,
        bool reachedFailureLimit = true,
        bool shouldRequestRetry = true)
    {
        var repository = new FakeBackfillRepository
        {
            ClaimedChannel = claimedChannel,
            ReachedFailureLimit = reachedFailureLimit,
            ShouldRequestRetry = shouldRequestRetry
        };
        var gateway = new FakeYouTubeBackfillGateway
        {
            PageResult = page ?? UploadsPageResult.Succeeded([], null),
            StatsResult = stats ?? VideoStatsBatchResult.Succeeded([])
        };
        var quota = new FakeDailyQuotaRepository
        {
            QueriesReservation = queriesReservation,
            BatchStatsReservation = batchStatsReservation
        };
        var logger = new FakeActivityLogger();

        var useCase = new BackfillUseCase(
            repository,
            gateway,
            quota,
            logger,
            new FakePageLimitPolicy(),
            new FixedTimeProvider(Now));

        return (useCase, repository, gateway, quota, logger);
    }

    private static Channel ClaimedChannel(
        string? uploadsPlaylistId = UploadsPlaylistId,
        int pagesCompleted = 0,
        string? pageToken = null,
        DateOnly? firstPageQuotaDate = null) => new()
    {
        Id = Guid.NewGuid(),
        YoutubeChannelId = "UC-channel",
        Name = "Channel",
        Status = ChannelStatus.APPROVED,
        BackfillStatus = BackfillStatus.RUNNING,
        UploadsPlaylistId = uploadsPlaylistId,
        BackfillPagesCompleted = pagesCompleted,
        BackfillPageToken = pageToken,
        FirstPageQuotaDate = firstPageQuotaDate ?? (pagesCompleted == 0 ? QuotaDate : null),
        BackfillClaimToken = Guid.NewGuid(),
        BackfillClaimedAt = Now,
        IsDeleted = false,
        ApprovedAt = Now.AddDays(-1),
        DiscoveredAt = Now,
        CreatedAt = Now,
        ModifiedAt = Now
    };

    private static UploadsPageItemResult UploadItem(string youtubeVideoId) =>
        new(youtubeVideoId, $"Title {youtubeVideoId}", null, Now.AddDays(-2));

    private static VideoStatsItemResult StatsItem(string youtubeVideoId, long durationMillis = 30_000) =>
        new(youtubeVideoId, durationMillis, 1_000, 50);

    private sealed class FixedTimeProvider(DateTimeOffset utcNow) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => utcNow;
    }

    private sealed class FakePageLimitPolicy : IBackfillPageLimitPolicy
    {
        public int PageLimit => 3;
    }

    private sealed class FakeBackfillRepository : IBackfillRepository
    {
        public Channel? ClaimedChannel { get; init; }

        public bool ReachedFailureLimit { get; init; } = true;

        public bool ShouldRequestRetry { get; init; } = true;

        public int ClaimCount { get; private set; }

        public int PersistCount { get; private set; }

        public int FailureCount { get; private set; }

        public BackfillPageResult? PersistedPage { get; private set; }

        public List<(string Operation, string Reason)> Failures { get; } = [];

        public List<(Guid ChannelId, QuotaLimitBucket Bucket)> DeferredForQuota { get; } = [];

        public Task<Channel?> TryClaimPageAsync(Guid channelId, Guid claimToken, DateOnly quotaDate, BackfillTriggerSource triggerSource, DateTimeOffset utcNow, CancellationToken cancellationToken)
        {
            ClaimCount++;
            return Task.FromResult(ClaimedChannel);
        }

        public Task<BackfillPagePersistResult> PersistPageAsync(BackfillPageResult page, CancellationToken cancellationToken)
        {
            PersistCount++;
            PersistedPage = page;
            return Task.FromResult(new BackfillPagePersistResult(0, 0, BackfillStatus.PENDING, page.NextPageToken is not null));
        }

        public Task<bool> DeferForQuotaLimitAsync(Guid channelId, Guid claimToken, QuotaLimitBucket bucket, DateTimeOffset utcNow, CancellationToken cancellationToken)
        {
            DeferredForQuota.Add((channelId, bucket));
            return Task.FromResult(true);
        }

        public Task<BackfillFailureResult?> RecordFailureAsync(Guid channelId, Guid claimToken, string failureReason, DateTimeOffset utcNow, CancellationToken cancellationToken)
        {
            FailureCount++;
            Failures.Add((string.Empty, failureReason));
            return Task.FromResult<BackfillFailureResult?>(new BackfillFailureResult(
                1,
                ReachedFailureLimit,
                ReachedFailureLimit ? BackfillStatus.FAILED : BackfillStatus.PENDING,
                ShouldRequestRetry));
        }

        public Task ReleaseClaimAsync(Guid channelId, Guid claimToken, DateTimeOffset utcNow, CancellationToken cancellationToken) =>
            Task.CompletedTask;

        public Task<BackfillRecoveryResult> RecoverStaleWorkAsync(DateTimeOffset staleBeforeUtc, DateTimeOffset utcNow, int maximumCount, CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public Task<BackfillDispatchResult> DispatchCandidatesAsync(int pageLimit, DateTimeOffset utcNow, int maximumCount, CancellationToken cancellationToken) =>
            throw new NotSupportedException();
    }

    private sealed class FakeYouTubeBackfillGateway : IYouTubeBackfillGateway
    {
        public UploadsPageResult PageResult { get; init; } = UploadsPageResult.Succeeded([], null);

        public VideoStatsBatchResult StatsResult { get; init; } = VideoStatsBatchResult.Succeeded([]);

        public int PageCallCount { get; private set; }

        public int StatsCallCount { get; private set; }

        public Task<UploadsPageResult> GetUploadsPageAsync(string uploadsPlaylistId, string? pageToken, CancellationToken cancellationToken)
        {
            PageCallCount++;
            return Task.FromResult(PageResult);
        }

        public Task<VideoStatsBatchResult> GetVideoStatsAsync(IReadOnlyCollection<string> youtubeVideoIds, CancellationToken cancellationToken)
        {
            StatsCallCount++;
            return Task.FromResult(StatsResult);
        }

        public Task<UploadsPlaylistResult> ResolveUploadsPlaylistAsync(string youtubeChannelId, CancellationToken cancellationToken) =>
            throw new NotSupportedException();
    }

    private sealed class FakeDailyQuotaRepository : IDailyQuotaRepository
    {
        public QuotaReservationResult QueriesReservation { get; init; } = QuotaReservationResult.Reserved;

        public QuotaReservationResult BatchStatsReservation { get; init; } = QuotaReservationResult.Reserved;

        public int QueriesReservations { get; private set; }

        public int BatchStatsReservations { get; private set; }

        public int QueriesExhaustedMarks { get; private set; }

        public int BatchStatsExhaustedMarks { get; private set; }

        public Task<QuotaReservationResult> TryReserveQueriesUnitAsync(DateOnly quotaDate, Guid? dailyProcessClaimToken, CancellationToken cancellationToken)
        {
            QueriesReservations++;
            return Task.FromResult(QueriesReservation);
        }

        public Task<QuotaReservationResult> TryReserveVideoBatchStatsUnitAsync(DateOnly quotaDate, Guid? dailyProcessClaimToken, CancellationToken cancellationToken)
        {
            BatchStatsReservations++;
            return Task.FromResult(BatchStatsReservation);
        }

        public Task MarkQueriesQuotaExhaustedAsync(DateOnly quotaDate, DateTimeOffset utcNow, CancellationToken cancellationToken)
        {
            QueriesExhaustedMarks++;
            return Task.CompletedTask;
        }

        public Task MarkVideoBatchStatsQuotaExhaustedAsync(DateOnly quotaDate, DateTimeOffset utcNow, CancellationToken cancellationToken)
        {
            BatchStatsExhaustedMarks++;
            return Task.CompletedTask;
        }

        public Task<YouTubeDailyQuotaUsage?> GetForDateAsync(DateOnly quotaDate, CancellationToken cancellationToken) =>
            Task.FromResult<YouTubeDailyQuotaUsage?>(null);

        public Task EnsureLedgerAsync(DateOnly quotaDate, CancellationToken cancellationToken) => Task.CompletedTask;

        public Task<DailyProcessClaimDecision> TryClaimDailyProcessAsync(DateOnly quotaDate, Guid claimToken, DateTimeOffset utcNow, CancellationToken cancellationToken) =>
            Task.FromResult(DailyProcessClaimDecision.AlreadyRunning);

        public Task ReleaseDailyProcessClaimAsync(DateOnly quotaDate, Guid claimToken, CancellationToken cancellationToken) => Task.CompletedTask;

        public Task<DailyFinalizationResult> FinalizeDailyProcessingAsync(DateOnly quotaDate, Guid claimToken, DateTimeOffset utcNow, CancellationToken cancellationToken) =>
            Task.FromResult(new DailyFinalizationResult(false, false, 0, 0));

        public Task<QuotaReservationResult> TryReserveSearchUnitAsync(DateOnly quotaDate, Guid? dailyProcessClaimToken, CancellationToken cancellationToken) =>
            Task.FromResult(QuotaReservationResult.Reserved);

        public Task MarkSearchQuotaExhaustedAsync(DateOnly quotaDate, DateTimeOffset utcNow, CancellationToken cancellationToken) => Task.CompletedTask;
    }

    private sealed class FakeActivityLogger : IBackfillActivityLogger
    {
        public List<string> Events { get; } = [];

        public void LogFirstPageProviderFailed(Guid channelId, string operation, string reason) => Events.Add($"ProviderFailed:{operation}");
        public void LogFirstPageFailed(Guid channelId, int failureCount, int failureLimit, string reason) => Events.Add($"Failed:{failureCount}");
        public void LogFirstPageQuotaExceeded(Guid channelId, DateOnly quotaDate, string operation) => Events.Add($"QuotaExceeded:{operation}");
        public void LogFailureStateWriteFailed(Guid channelId, string operation, Exception exception) => Events.Add($"FailureStateWriteFailed:{operation}");
        public void LogPersistenceFailed(Guid channelId, string operation, Exception exception) => Events.Add($"PersistenceFailed:{operation}");
        public void LogSchedulingFailed(Guid channelId, Exception exception) => Events.Add("SchedulingFailed");
        public void LogSchedulingStateWriteFailed(Guid channelId, Exception exception) => Events.Add("SchedulingStateWriteFailed");
        public void LogTerminalPageFailure(Guid channelId, int failureCount, string reason) => Events.Add($"Terminal:{failureCount}");
        public void LogRetentionCleared(int deletedCount, int batchCount) => Events.Add("RetentionCleared");
    }
}
