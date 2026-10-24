using YtFetcher.Application.Discovery.DTO.Result;
using YtFetcher.Core.Discovery;

namespace YtFetcher.Application.Abstractions.Repositories;

/// <summary>
/// Persistence boundary for discovery reads and per-keyword persistence. Implemented in Infrastructure.
/// </summary>
public interface IDiscoveryRepository
{
    /// <summary>Returns active, non-deleted keywords in a deterministic order.</summary>
    Task<IReadOnlyList<DiscoveryKeyword>> GetActiveKeywordsAsync(CancellationToken cancellationToken);

    /// <summary>Returns the existing discovery state rows for the given keywords.</summary>
    Task<IReadOnlyList<DiscoveryState>> GetStatesForKeywordsAsync(
        IReadOnlyCollection<Guid> keywordIds,
        CancellationToken cancellationToken);

    /// <summary>
    /// Persists channels, provenance, and the advanced keyword state for one keyword page in a single save.
    /// </summary>
    Task<DiscoveryPersistResult> PersistKeywordDiscoveryAsync(
        DiscoveryKeyword keyword,
        IReadOnlyList<YouTubeChannelCandidate> candidates,
        string? nextPageToken,
        DateTimeOffset utcNow,
        CancellationToken cancellationToken,
        DateOnly quotaDate);
}

