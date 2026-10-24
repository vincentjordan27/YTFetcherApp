namespace YtFetcher.Application.Discovery.DTO.Result;

/// <summary>
/// Sanitized outcome of one discovery <c>channels.list</c> metadata lookup. Application reserves one Queries
/// unit before each lookup chunk.
/// </summary>
public sealed record YouTubeChannelMetadataResult(
    bool Succeeded,
    string? FailureReason,
    IReadOnlyList<YouTubeChannelMetadata> Items)
{
    public static YouTubeChannelMetadataResult Failed(string reason) => new(false, reason, []);

    public static YouTubeChannelMetadataResult Completed(IReadOnlyList<YouTubeChannelMetadata> items) =>
        new(true, null, items);
}
