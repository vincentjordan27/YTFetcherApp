using YtFetcher.Application.Abstractions.UseCases;
using YtFetcher.Application.Discovery.DTO.Result;
using YtFetcher.Infrastructure.Jobs;

namespace YtFetcher.Api.Tests.Discovery;

public class ChannelDiscoveryHangfireJobTests
{
    [Fact]
    public async Task Job_invokes_the_use_case_once()
    {
        var summary = new ChannelDiscoveryRunSummary(
            ActiveKeywords: 9,
            SuccessfulKeywords: 8,
            FailedKeywords: 1,
            SearchItemsReceived: 100,
            MalformedItemsSkipped: 2,
            ChannelMetadataMisses: 1,
            ChannelsCreated: 3,
            ChannelsRefreshed: 4,
            SoftDeletedChannelsIgnored: 1,
            ProvenanceRowsCreated: 5,
            DuplicateProvenanceRows: 2,
            FailedKeywordReasons: ["QuotaExceeded"]);

        var useCase = new FakeRunChannelDiscoveryUseCase(summary);
        var job = new ChannelDiscoveryHangfireJob(useCase);

        await job.RunAsync(CancellationToken.None);

        Assert.Equal(1, useCase.CallCount);
    }

    [Fact]
    public async Task Job_does_not_swallow_an_unexpected_use_case_exception()
    {
        var useCase = new FakeRunChannelDiscoveryUseCase(exception: new InvalidOperationException("infrastructure down"));
        var job = new ChannelDiscoveryHangfireJob(useCase);

        await Assert.ThrowsAsync<InvalidOperationException>(() => job.RunAsync(CancellationToken.None));
        Assert.Equal(1, useCase.CallCount);
    }

    private sealed class FakeRunChannelDiscoveryUseCase(
        ChannelDiscoveryRunSummary? summary = null,
        Exception? exception = null) : IRunChannelDiscoveryUseCase
    {
        public int CallCount { get; private set; }

        public Task<ChannelDiscoveryRunSummary> RunAsync(CancellationToken cancellationToken)
        {
            CallCount++;

            return exception is not null
                ? Task.FromException<ChannelDiscoveryRunSummary>(exception)
                : Task.FromResult(summary!);
        }
    }
}
