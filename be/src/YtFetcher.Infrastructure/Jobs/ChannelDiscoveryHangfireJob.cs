using Hangfire;
using YtFetcher.Application.Abstractions.UseCases;

namespace YtFetcher.Infrastructure.Jobs;

/// <summary>
/// Hangfire adapter. The use case owns the single aggregate discovery summary; this adapter only invokes it once.
/// </summary>
public sealed class ChannelDiscoveryHangfireJob(IRunChannelDiscoveryUseCase runChannelDiscoveryUseCase)
{
    [AutomaticRetry(Attempts = 0)]
    public async Task RunAsync(CancellationToken cancellationToken)
    {
        await runChannelDiscoveryUseCase.RunAsync(cancellationToken);
    }
}
