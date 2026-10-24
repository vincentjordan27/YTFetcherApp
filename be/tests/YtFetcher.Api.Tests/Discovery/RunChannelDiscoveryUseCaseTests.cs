using YtFetcher.Application.Abstractions.Repositories;
using YtFetcher.Application.Abstractions.Services;
using YtFetcher.Application.Discovery.DTO.Result;
using YtFetcher.Application.Discovery.UseCases;
using YtFetcher.Application.Quotas.DTO.Result;
using YtFetcher.Core.Discovery;
using YtFetcher.Core.Quotas;

namespace YtFetcher.Api.Tests.Discovery;

/// <summary>
/// Discovery now reserves one Search unit before <c>search.list</c> and one Queries unit before each metadata
/// chunk. These tests pin the bucket split, the safe failure outcomes, and the no-persist/no-token-advance
/// behavior when a reservation is refused.
/// </summary>
public class RunChannelDiscoveryUseCaseTests
{
    private static readonly DateTimeOffset Now = new(2026, 9, 22, 0, 5, 0, TimeSpan.Zero);

    [Fact]
    public async Task Empty_active_keyword_set_succeeds_with_zero_counts()
    {
        var repository = new FakeDiscoveryRepository();
        var gateway = new FakeGateway();
        var logger = new FakeActivityLogger();
        var useCase = CreateUseCase(repository, gateway, logger, new FakeDailyQuotaRepository());

        var summary = await useCase.RunAsync(CancellationToken.None);

        Assert.Equal(0, summary.ActiveKeywords);
        Assert.Equal(0, summary.SuccessfulKeywords);
        Assert.Equal(0, summary.FailedKeywords);
        Assert.Empty(summary.FailedKeywordReasons);
        Assert.Empty(repository.PersistCalls);
        Assert.Empty(gateway.Calls);
        Assert.Single(logger.RunCompletions);
        Assert.Empty(logger.KeywordFailures);
    }

    [Fact]
    public async Task Valid_result_reserves_search_then_queries_and_persists_composed_candidates()
    {
        var keyword = Keyword("sakura");
        var repository = new FakeDiscoveryRepository
        {
            ActiveKeywords = [keyword],
            States =
            [
                new DiscoveryState { Id = Guid.NewGuid(), KeywordId = keyword.Id, RecentPageToken = "previous-token" }
            ],
            PersistResult = new DiscoveryPersistResult(1, 1, 0, 2, 3)
        };
        var gateway = new FakeGateway()
            .WithSearch(keyword.Keyword, YouTubeDiscoverySearchResult.Completed(
                [SearchItem("UC-1", "video-1"), SearchItem("UC-2", "video-2"), SearchItem("UC-3", "video-3")],
                malformedItemsSkipped: 1,
                nextPageToken: "next-token"))
            .WithMetadata("UC-1")
            .WithMetadata("UC-2");
        var logger = new FakeActivityLogger();
        var quota = new FakeDailyQuotaRepository();
        var useCase = CreateUseCase(repository, gateway, logger, quota);

        var summary = await useCase.RunAsync(CancellationToken.None);

        Assert.Equal(1, summary.ActiveKeywords);
        Assert.Equal(1, summary.SuccessfulKeywords);
        Assert.Equal(0, summary.FailedKeywords);
        Assert.Equal(3, summary.SearchItemsReceived);
        Assert.Equal(1, summary.MalformedItemsSkipped);
        Assert.Equal(1, summary.ChannelMetadataMisses);
        Assert.Equal(1, summary.ChannelsCreated);
        Assert.Equal(1, summary.ChannelsRefreshed);
        Assert.Equal(2, summary.ProvenanceRowsCreated);
        Assert.Equal(3, summary.DuplicateProvenanceRows);
        Assert.Empty(summary.FailedKeywordReasons);

        var gatewayCall = Assert.Single(gateway.Calls);
        Assert.Equal("sakura", gatewayCall.Keyword);
        Assert.Equal("previous-token", gatewayCall.PageToken);

        var persistCall = Assert.Single(repository.PersistCalls);
        Assert.Equal(keyword.Id, persistCall.KeywordId);
        Assert.Equal(2, persistCall.Candidates.Count);
        Assert.Equal("next-token", persistCall.NextPageToken);

        Assert.Equal(1, quota.SearchReservations);
        Assert.Equal(1, quota.QueriesReservations);
        Assert.Single(logger.RunCompletions);
    }

    [Fact]
    public async Task Missing_state_uses_a_null_page_token()
    {
        var keyword = Keyword("sakura");
        var repository = new FakeDiscoveryRepository { ActiveKeywords = [keyword] };
        var gateway = new FakeGateway().WithSearch(
            keyword.Keyword,
            YouTubeDiscoverySearchResult.Completed([], 0, null));
        var useCase = CreateUseCase(repository, gateway, new FakeActivityLogger(), new FakeDailyQuotaRepository());

        await useCase.RunAsync(CancellationToken.None);

        Assert.Null(Assert.Single(gateway.Calls).PageToken);
    }

    [Fact]
    public async Task Failed_keyword_does_not_prevent_later_keywords_and_marks_the_search_bucket()
    {
        var failing = Keyword("failing");
        var succeeding = Keyword("succeeding");
        var repository = new FakeDiscoveryRepository
        {
            ActiveKeywords = [failing, succeeding],
            PersistResult = new DiscoveryPersistResult(1, 0, 0, 1, 0)
        };
        var gateway = new FakeGateway()
            .WithSearch(failing.Keyword, YouTubeDiscoverySearchResult.Failed("QuotaExceeded"))
            .WithSearch(succeeding.Keyword, YouTubeDiscoverySearchResult.Completed([SearchItem("UC-1", "video-1")], 0, null))
            .WithMetadata("UC-1");
        var quota = new FakeDailyQuotaRepository();
        var useCase = CreateUseCase(repository, gateway, new FakeActivityLogger(), quota);

        var summary = await useCase.RunAsync(CancellationToken.None);

        Assert.Equal(2, summary.ActiveKeywords);
        Assert.Equal(1, summary.FailedKeywords);
        Assert.Equal(1, summary.SuccessfulKeywords);
        Assert.Equal(new[] { "QuotaExceeded" }, summary.FailedKeywordReasons);
        Assert.Equal(1, summary.ChannelsCreated);
        Assert.Equal(1, quota.SearchExhaustedMarks);

        var persistCall = Assert.Single(repository.PersistCalls);
        Assert.Equal(succeeding.Id, persistCall.KeywordId);
    }

    [Fact]
    public async Task Missing_gateway_failure_reason_is_replaced_with_a_sanitized_category()
    {
        var keyword = Keyword("sakura");
        var repository = new FakeDiscoveryRepository { ActiveKeywords = [keyword] };
        var gateway = new FakeGateway().WithSearch(keyword.Keyword, YouTubeDiscoverySearchResult.Failed("   "));
        var useCase = CreateUseCase(repository, gateway, new FakeActivityLogger(), new FakeDailyQuotaRepository());

        var summary = await useCase.RunAsync(CancellationToken.None);

        Assert.Equal(new[] { "UnknownFailure" }, summary.FailedKeywordReasons);
    }

    [Fact]
    public async Task A_refused_queries_reservation_prevents_metadata_lookup_and_persistence()
    {
        var keyword = Keyword("sakura");
        var repository = new FakeDiscoveryRepository { ActiveKeywords = [keyword] };
        var gateway = new FakeGateway().WithSearch(
            keyword.Keyword,
            YouTubeDiscoverySearchResult.Completed([SearchItem("UC-1", "video-1")], 0, "next-token"));
        var quota = new FakeDailyQuotaRepository { QueriesReservation = QuotaReservationResult.NoRemainingCapacity };
        var useCase = CreateUseCase(repository, gateway, new FakeActivityLogger(), quota);

        var summary = await useCase.RunAsync(CancellationToken.None);

        Assert.Equal(0, summary.SuccessfulKeywords);
        Assert.Equal(1, summary.FailedKeywords);
        Assert.Equal(new[] { "QuotaLimitReached" }, summary.FailedKeywordReasons);
        Assert.Empty(repository.PersistCalls);
        Assert.Equal(0, gateway.MetadataCallCount);
    }

    private static RunChannelDiscoveryUseCase CreateUseCase(
        FakeDiscoveryRepository repository,
        FakeGateway gateway,
        FakeActivityLogger logger,
        FakeDailyQuotaRepository quota) =>
        new(repository, gateway, quota, logger, new FixedTimeProvider(Now), new FixedPageLimitPolicy(1));

    private static DiscoveryKeyword Keyword(string text) => new()
    {
        Id = Guid.NewGuid(),
        Keyword = text,
        IsActive = true,
        IsDeleted = false,
        CreatedAt = Now.AddDays(-1),
        ModifiedAt = Now.AddDays(-1)
    };

    private static YouTubeDiscoverySearchItem SearchItem(string channelId, string videoId) => new(
        channelId,
        videoId,
        $"Video {videoId}",
        $"https://example.com/{videoId}.jpg",
        Now.AddDays(-3));

    private sealed class FakeDiscoveryRepository : IDiscoveryRepository
    {
        public IReadOnlyList<DiscoveryKeyword> ActiveKeywords { get; init; } = [];

        public IReadOnlyList<DiscoveryState> States { get; init; } = [];

        public DiscoveryPersistResult PersistResult { get; init; } = new(0, 0, 0, 0, 0);

        public List<PersistCall> PersistCalls { get; } = [];

        public Task<IReadOnlyList<DiscoveryKeyword>> GetActiveKeywordsAsync(CancellationToken cancellationToken) =>
            Task.FromResult(ActiveKeywords);

        public Task<IReadOnlyList<DiscoveryState>> GetStatesForKeywordsAsync(
            IReadOnlyCollection<Guid> keywordIds,
            CancellationToken cancellationToken) => Task.FromResult(States);

        public Task<DiscoveryPersistResult> PersistKeywordDiscoveryAsync(
            DiscoveryKeyword keyword,
            IReadOnlyList<YouTubeChannelCandidate> candidates,
            string? nextPageToken,
            DateTimeOffset utcNow,
            CancellationToken cancellationToken,
            DateOnly quotaDate)
        {
            PersistCalls.Add(new PersistCall(keyword.Id, candidates, nextPageToken, utcNow));
            return Task.FromResult(PersistResult);
        }
    }

    private sealed class FakeGateway : IYouTubeChannelDiscoveryGateway
    {
        private readonly Dictionary<string, YouTubeDiscoverySearchResult> searchResults = [];
        private readonly HashSet<string> knownChannels = [];

        public List<GatewayCall> Calls { get; } = [];

        public int MetadataCallCount { get; private set; }

        public FakeGateway WithSearch(string keyword, YouTubeDiscoverySearchResult result)
        {
            searchResults[keyword] = result;
            return this;
        }

        public FakeGateway WithMetadata(string channelId)
        {
            knownChannels.Add(channelId);
            return this;
        }

        public Task<YouTubeDiscoverySearchResult> SearchAsync(
            string keyword,
            string? pageToken,
            CancellationToken cancellationToken)
        {
            Calls.Add(new GatewayCall(keyword, pageToken));
            return Task.FromResult(searchResults.TryGetValue(keyword, out var result)
                ? result
                : YouTubeDiscoverySearchResult.Failed("NotConfigured"));
        }

        public Task<YouTubeChannelMetadataResult> GetChannelMetadataAsync(
            IReadOnlyCollection<string> channelIds,
            CancellationToken cancellationToken)
        {
            MetadataCallCount++;
            var items = channelIds
                .Where(knownChannels.Contains)
                .Select(channelId => new YouTubeChannelMetadata(
                    channelId,
                    $"Channel {channelId}",
                    $"https://example.com/{channelId}.jpg",
                    $"UU-{channelId}"))
                .ToList();

            return Task.FromResult(YouTubeChannelMetadataResult.Completed(items));
        }
    }

    private sealed class FakeDailyQuotaRepository : IDailyQuotaRepository
    {
        public int SearchReservations { get; private set; }

        public int QueriesReservations { get; private set; }

        public int SearchExhaustedMarks { get; private set; }

        public QuotaReservationResult QueriesReservation { get; init; } = QuotaReservationResult.Reserved;

        public Task<YouTubeDailyQuotaUsage?> GetForDateAsync(DateOnly quotaDate, CancellationToken cancellationToken) =>
            Task.FromResult<YouTubeDailyQuotaUsage?>(null);

        public Task EnsureLedgerAsync(DateOnly quotaDate, CancellationToken cancellationToken) => Task.CompletedTask;

        public Task<DailyProcessClaimDecision> TryClaimDailyProcessAsync(DateOnly quotaDate, Guid claimToken, DateTimeOffset utcNow, CancellationToken cancellationToken) =>
            Task.FromResult(DailyProcessClaimDecision.AlreadyRunning);

        public Task ReleaseDailyProcessClaimAsync(DateOnly quotaDate, Guid claimToken, CancellationToken cancellationToken) => Task.CompletedTask;

        public Task<DailyFinalizationResult> FinalizeDailyProcessingAsync(DateOnly quotaDate, Guid claimToken, DateTimeOffset utcNow, CancellationToken cancellationToken) =>
            Task.FromResult(new DailyFinalizationResult(false, false, 0, 0));

        public Task<QuotaReservationResult> TryReserveQueriesUnitAsync(DateOnly quotaDate, Guid? dailyProcessClaimToken, CancellationToken cancellationToken)
        {
            QueriesReservations++;
            return Task.FromResult(QueriesReservation);
        }

        public Task<QuotaReservationResult> TryReserveVideoBatchStatsUnitAsync(DateOnly quotaDate, Guid? dailyProcessClaimToken, CancellationToken cancellationToken) =>
            Task.FromResult(QuotaReservationResult.Reserved);

        public Task<QuotaReservationResult> TryReserveSearchUnitAsync(DateOnly quotaDate, Guid? dailyProcessClaimToken, CancellationToken cancellationToken)
        {
            SearchReservations++;
            return Task.FromResult(QuotaReservationResult.Reserved);
        }

        public Task MarkSearchQuotaExhaustedAsync(DateOnly quotaDate, DateTimeOffset utcNow, CancellationToken cancellationToken)
        {
            SearchExhaustedMarks++;
            return Task.CompletedTask;
        }

        public Task MarkQueriesQuotaExhaustedAsync(DateOnly quotaDate, DateTimeOffset utcNow, CancellationToken cancellationToken) => Task.CompletedTask;

        public Task MarkVideoBatchStatsQuotaExhaustedAsync(DateOnly quotaDate, DateTimeOffset utcNow, CancellationToken cancellationToken) => Task.CompletedTask;
    }

    private sealed class FakeActivityLogger : IDiscoveryActivityLogger
    {
        public List<RunCompletion> RunCompletions { get; } = [];

        public List<KeywordFailure> KeywordFailures { get; } = [];

        public void LogRunCompleted(ChannelDiscoveryRunSummary summary, TimeSpan duration) =>
            RunCompletions.Add(new RunCompletion(summary, duration));

        public void LogKeywordFailed(DiscoveryKeyword keyword, string failureReason, TimeSpan duration) =>
            KeywordFailures.Add(new KeywordFailure(keyword.Id, failureReason));
    }

    private sealed class FixedTimeProvider(DateTimeOffset utcNow) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => utcNow;
    }

    private sealed class FixedPageLimitPolicy(int pageLimit) : IDiscoveryPageLimitPolicy
    {
        public int PageLimit { get; } = pageLimit;
    }

    private sealed record PersistCall(
        Guid KeywordId,
        IReadOnlyList<YouTubeChannelCandidate> Candidates,
        string? NextPageToken,
        DateTimeOffset UtcNow);

    private sealed record GatewayCall(string Keyword, string? PageToken);

    private sealed record RunCompletion(ChannelDiscoveryRunSummary Summary, TimeSpan Duration);

    private sealed record KeywordFailure(Guid KeywordId, string FailureReason);
}
