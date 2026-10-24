using YtFetcher.Application.Discovery.DTO.Result;
using YtFetcher.Core.Discovery;

namespace YtFetcher.Application.Abstractions.Services;

/// <summary>
/// Observability boundary for one discovery run. Implemented in Infrastructure.
/// </summary>
public interface IDiscoveryActivityLogger
{
    void LogRunCompleted(ChannelDiscoveryRunSummary summary, TimeSpan duration);

    void LogKeywordFailed(DiscoveryKeyword keyword, string failureReason, TimeSpan duration);
}
