using YtFetcher.Application.Discovery.DTO.Result;

namespace YtFetcher.Application.Abstractions.Services;

/// <summary>
/// YouTube discovery gateway boundary, split into the two provider calls that consume different quota buckets.
/// Application reserves one Search unit before <see cref="SearchAsync"/> and one Queries unit before each
/// <see cref="GetChannelMetadataAsync"/> chunk, so the gateway itself never touches the ledger.
/// </summary>
public interface IYouTubeChannelDiscoveryGateway
{
    /// <summary>Runs one <c>search.list</c> page for the keyword.</summary>
    Task<YouTubeDiscoverySearchResult> SearchAsync(
        string keyword,
        string? pageToken,
        CancellationToken cancellationToken);

    /// <summary>Runs one <c>channels.list</c> metadata lookup for a single channel-ID chunk.</summary>
    Task<YouTubeChannelMetadataResult> GetChannelMetadataAsync(
        IReadOnlyCollection<string> channelIds,
        CancellationToken cancellationToken);
}
