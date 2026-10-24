namespace YtFetcher.Infrastructure.YouTube.DTO.Request;

/// <summary>
/// Outbound parameters of one YouTube <c>playlistItems.list</c> call for a channel's uploads
/// playlist. Infrastructure-only. It carries the API key, so it is a class instead of a record for
/// the same reason as <see cref="YouTubeSearchListRequest"/>.
/// </summary>
internal sealed class YouTubePlaylistItemsListRequest
{
    public required string UploadsPlaylistId { get; init; }

    /// <summary>
    /// Durable continuation cursor. <c>null</c> requests the newest page and a value resumes from that
    /// cursor, so it is optional rather than <c>required</c>.
    /// </summary>
    public string? PageToken { get; init; }

    public required int MaxResults { get; init; }

    public required string ApiKey { get; init; }
}
