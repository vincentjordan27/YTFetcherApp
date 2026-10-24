using YtFetcher.Application.Discovery.DTO.Result;

namespace YtFetcher.Application.Abstractions.UseCases;

/// <summary>
/// Application use-case boundary for one scheduled channel-discovery run.
/// </summary>
public interface IRunChannelDiscoveryUseCase
{
    Task<ChannelDiscoveryRunSummary> RunAsync(CancellationToken cancellationToken);
}
