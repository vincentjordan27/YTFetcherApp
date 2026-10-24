using YtFetcher.Application.Abstractions.Repositories;
using YtFetcher.Application.Abstractions.Services;
using YtFetcher.Application.Discovery.DTO.Result;
using YtFetcher.Application.Discovery.UseCases;
using YtFetcher.Application.Quotas;
using YtFetcher.Application.Quotas.DTO.Result;
using YtFetcher.Core.Discovery;
using YtFetcher.Core.Quotas;

namespace YtFetcher.Api.Tests.UseCases;

public sealed class RunChannelDiscoveryUseCaseTests
{
    private static readonly DateTimeOffset Now = new(2026, 9, 22, 0, 5, 0, TimeSpan.Zero);
    private static readonly DateOnly QuotaDate = PacificQuotaWindow.GetQuotaDate(Now);

    [Fact]
    public async Task Unlimited_run_repeats_keyword_passes_until_each_keyword_reaches_terminal_page()
    {
        var first = Keyword("first");
        var second = Keyword("second");
        var repository = new FakeDiscoveryRepository([first, second]);
        var gateway = new ScriptedGateway((keyword, token) =>
            YouTubeDiscoverySearchResult.Completed([], 0, token is null ? $"{keyword}-page-2" : null));

        await CreateUseCase(repository, gateway, new FakeDailyQuotaRepository()).RunAsync(CancellationToken.None);

        Assert.Equal(
            [
                new SearchCall("first", null),
                new SearchCall("second", null),
                new SearchCall("first", "first-page-2"),
                new SearchCall("second", "second-page-2")
            ],
            gateway.Calls);
        Assert.Equal(4, repository.PersistCalls.Count);
        Assert.All(repository.PersistCalls, call => Assert.Equal(QuotaDate, call.QuotaDate));
    }

    [Fact]
    public async Task Page_limit_one_processes_each_keyword_once_and_retains_its_next_page_token()
    {
        var first = Keyword("first");
        var second = Keyword("second");
        var repository = new FakeDiscoveryRepository([first, second]);
        var gateway = new ScriptedGateway((keyword, _) =>
            YouTubeDiscoverySearchResult.Completed([], 0, $"{keyword}-page-2"));

        await CreateUseCase(repository, gateway, new FakeDailyQuotaRepository(), pageLimit: 1)
            .RunAsync(CancellationToken.None);

        Assert.Equal(
            [new SearchCall("first", null), new SearchCall("second", null)],
            gateway.Calls);
        Assert.Equal(
            ["first-page-2", "second-page-2"],
            repository.PersistCalls.Select(call => call.NextPageToken));
    }

    [Fact]
    public async Task Keyword_completed_on_current_quota_date_is_skipped()
    {
        var completed = Keyword("completed");
        var eligible = Keyword("eligible");
        var repository = new FakeDiscoveryRepository(
            [completed, eligible],
            [new DiscoveryState
            {
                Id = Guid.NewGuid(),
                KeywordId = completed.Id,
                RecentCompletedQuotaDate = QuotaDate
            }]);
        var gateway = new ScriptedGateway((_, _) => YouTubeDiscoverySearchResult.Completed([], 0, null));

        await CreateUseCase(repository, gateway, new FakeDailyQuotaRepository()).RunAsync(CancellationToken.None);

        Assert.Equal([new SearchCall("eligible", null)], gateway.Calls);
    }

    [Fact]
    public async Task Failed_keyword_is_not_retried_in_later_passes_and_later_keyword_continues()
    {
        var failing = Keyword("failing");
        var continuing = Keyword("continuing");
        var repository = new FakeDiscoveryRepository([failing, continuing]);
        var gateway = new ScriptedGateway((keyword, token) =>
        {
            if (keyword == failing.Keyword)
            {
                return YouTubeDiscoverySearchResult.Failed("TemporaryFailure");
            }

            return YouTubeDiscoverySearchResult.Completed([], 0, token is null ? "page-2" : null);
        });

        await CreateUseCase(repository, gateway, new FakeDailyQuotaRepository()).RunAsync(CancellationToken.None);

        Assert.Equal(
            [
                new SearchCall("failing", null),
                new SearchCall("continuing", null),
                new SearchCall("continuing", "page-2")
            ],
            gateway.Calls);
    }

    [Fact]
    public async Task Search_reservation_refusal_stops_run_without_calling_provider_for_refused_unit()
    {
        var first = Keyword("first");
        var second = Keyword("second");
        var repository = new FakeDiscoveryRepository([first, second]);
        var gateway = new ScriptedGateway((_, _) =>
            YouTubeDiscoverySearchResult.Completed([], 0, "page-2"));
        var quota = new FakeDailyQuotaRepository(
            [QuotaReservationResult.Reserved, QuotaReservationResult.NoRemainingCapacity]);

        await CreateUseCase(repository, gateway, quota).RunAsync(CancellationToken.None);

        Assert.Equal([new SearchCall("first", null)], gateway.Calls);
        Assert.Equal(2, quota.SearchReservationCalls);
    }

    private static RunChannelDiscoveryUseCase CreateUseCase(
        FakeDiscoveryRepository repository,
        ScriptedGateway gateway,
        FakeDailyQuotaRepository quota,
        int pageLimit = 0) =>
        new(repository, gateway, quota, new FakeActivityLogger(), new FixedTimeProvider(Now), new PageLimitPolicy(pageLimit));

    private static DiscoveryKeyword Keyword(string value) => new()
    {
        Id = Guid.NewGuid(),
        Keyword = value,
        IsActive = true,
        CreatedAt = Now.AddDays(-1),
        ModifiedAt = Now.AddDays(-1)
    };

    private sealed class FakeDiscoveryRepository(
        IReadOnlyList<DiscoveryKeyword> keywords,
        IReadOnlyList<DiscoveryState>? states = null) : IDiscoveryRepository
    {
        public List<PersistCall> PersistCalls { get; } = [];

        public Task<IReadOnlyList<DiscoveryKeyword>> GetActiveKeywordsAsync(CancellationToken cancellationToken) =>
            Task.FromResult(keywords);

        public Task<IReadOnlyList<DiscoveryState>> GetStatesForKeywordsAsync(
            IReadOnlyCollection<Guid> keywordIds,
            CancellationToken cancellationToken) =>
            Task.FromResult(states ?? (IReadOnlyList<DiscoveryState>)[]);

        public Task<DiscoveryPersistResult> PersistKeywordDiscoveryAsync(
            DiscoveryKeyword keyword,
            IReadOnlyList<YouTubeChannelCandidate> candidates,
            string? nextPageToken,
            DateTimeOffset utcNow,
            CancellationToken cancellationToken,
            DateOnly quotaDate)
        {
            PersistCalls.Add(new PersistCall(keyword.Id, nextPageToken, quotaDate));
            return Task.FromResult(new DiscoveryPersistResult(0, 0, 0, 0, 0));
        }
    }

    private sealed class ScriptedGateway(
        Func<string, string?, YouTubeDiscoverySearchResult> search) : IYouTubeChannelDiscoveryGateway
    {
        public List<SearchCall> Calls { get; } = [];

        public Task<YouTubeDiscoverySearchResult> SearchAsync(
            string keyword,
            string? pageToken,
            CancellationToken cancellationToken)
        {
            Calls.Add(new SearchCall(keyword, pageToken));
            return Task.FromResult(search(keyword, pageToken));
        }

        public Task<YouTubeChannelMetadataResult> GetChannelMetadataAsync(
            IReadOnlyCollection<string> channelIds,
            CancellationToken cancellationToken) =>
            Task.FromResult(YouTubeChannelMetadataResult.Completed([]));
    }

    private sealed class FakeDailyQuotaRepository(IReadOnlyList<QuotaReservationResult>? searchResults = null) : IDailyQuotaRepository
    {
        private int searchResultIndex;

        public int SearchReservationCalls { get; private set; }

        public Task<YouTubeDailyQuotaUsage?> GetForDateAsync(DateOnly quotaDate, CancellationToken cancellationToken) =>
            Task.FromResult<YouTubeDailyQuotaUsage?>(null);

        public Task EnsureLedgerAsync(DateOnly quotaDate, CancellationToken cancellationToken) => Task.CompletedTask;

        public Task<DailyProcessClaimDecision> TryClaimDailyProcessAsync(
            DateOnly quotaDate,
            Guid claimToken,
            DateTimeOffset utcNow,
            CancellationToken cancellationToken) =>
            Task.FromResult(DailyProcessClaimDecision.AlreadyRunning);

        public Task ReleaseDailyProcessClaimAsync(DateOnly quotaDate, Guid claimToken, CancellationToken cancellationToken) => Task.CompletedTask;

        public Task<DailyFinalizationResult> FinalizeDailyProcessingAsync(
            DateOnly quotaDate,
            Guid claimToken,
            DateTimeOffset utcNow,
            CancellationToken cancellationToken) =>
            Task.FromResult(new DailyFinalizationResult(false, false, 0, 0));

        public Task<QuotaReservationResult> TryReserveQueriesUnitAsync(
            DateOnly quotaDate,
            Guid? dailyProcessClaimToken,
            CancellationToken cancellationToken) =>
            Task.FromResult(QuotaReservationResult.Reserved);

        public Task<QuotaReservationResult> TryReserveVideoBatchStatsUnitAsync(
            DateOnly quotaDate,
            Guid? dailyProcessClaimToken,
            CancellationToken cancellationToken) =>
            Task.FromResult(QuotaReservationResult.Reserved);

        public Task<QuotaReservationResult> TryReserveSearchUnitAsync(
            DateOnly quotaDate,
            Guid? dailyProcessClaimToken,
            CancellationToken cancellationToken)
        {
            SearchReservationCalls++;
            var result = searchResults is not null && searchResultIndex < searchResults.Count
                ? searchResults[searchResultIndex++]
                : QuotaReservationResult.Reserved;
            return Task.FromResult(result);
        }

        public Task MarkSearchQuotaExhaustedAsync(DateOnly quotaDate, DateTimeOffset utcNow, CancellationToken cancellationToken) => Task.CompletedTask;

        public Task MarkQueriesQuotaExhaustedAsync(DateOnly quotaDate, DateTimeOffset utcNow, CancellationToken cancellationToken) => Task.CompletedTask;

        public Task MarkVideoBatchStatsQuotaExhaustedAsync(DateOnly quotaDate, DateTimeOffset utcNow, CancellationToken cancellationToken) => Task.CompletedTask;
    }

    private sealed class FakeActivityLogger : IDiscoveryActivityLogger
    {
        public void LogRunCompleted(ChannelDiscoveryRunSummary summary, TimeSpan duration) { }

        public void LogKeywordFailed(DiscoveryKeyword keyword, string failureReason, TimeSpan duration) { }
    }

    private sealed class FixedTimeProvider(DateTimeOffset utcNow) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => utcNow;
    }

    private sealed class PageLimitPolicy(int pageLimit) : IDiscoveryPageLimitPolicy
    {
        public int PageLimit { get; } = pageLimit;
    }

    private sealed record SearchCall(string Keyword, string? PageToken);

    private sealed record PersistCall(Guid KeywordId, string? NextPageToken, DateOnly? QuotaDate);
}
