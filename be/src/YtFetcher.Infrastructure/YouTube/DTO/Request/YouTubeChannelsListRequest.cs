namespace YtFetcher.Infrastructure.YouTube.DTO.Request;

/// <summary>
/// Outbound parameters of one YouTube <c>channels.list</c> call for a batch of channel ids.
/// Infrastructure-only. It carries the API key, so it is a class instead of a record for the
/// same reason as <see cref="YouTubeSearchListRequest"/>.
/// </summary>
internal sealed class YouTubeChannelsListRequest
{
    public required IReadOnlyList<string> ChannelIds { get; init; }

    public required string ApiKey { get; init; }
}
