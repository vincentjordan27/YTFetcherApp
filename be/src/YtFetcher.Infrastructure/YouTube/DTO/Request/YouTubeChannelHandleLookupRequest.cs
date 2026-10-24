namespace YtFetcher.Infrastructure.YouTube.DTO.Request;

/// <summary>
/// Outbound parameters of one YouTube <c>channels.list</c> call filtered by <c>forHandle</c>.
/// Infrastructure-only. It carries the API key, so it is a class instead of a record for the same
/// reason as <see cref="YouTubeChannelsListRequest"/>.
/// </summary>
internal sealed class YouTubeChannelHandleLookupRequest
{
    public required string Handle { get; init; }

    public required string ApiKey { get; init; }
}
