namespace YtFetcher.Application.Discovery.DTO.Result;

/// <summary>
/// Sanitized outcome of one discovery <c>search.list</c> page. It carries the validated search items only;
/// channel metadata is fetched separately so Application can reserve the correct provider bucket before each
/// call. Failure reasons never contain API keys or raw payloads.
/// </summary>
public sealed record YouTubeDiscoverySearchResult(
    bool Succeeded,
    string? FailureReason,
    IReadOnlyList<YouTubeDiscoverySearchItem> Items,
    int MalformedItemsSkipped,
    string? NextPageToken)
{
    public static YouTubeDiscoverySearchResult Failed(string reason) =>
        new(false, reason, [], 0, null);

    public static YouTubeDiscoverySearchResult Completed(
        IReadOnlyList<YouTubeDiscoverySearchItem> items,
        int malformedItemsSkipped,
        string? nextPageToken) =>
        new(true, null, items, malformedItemsSkipped, nextPageToken);
}
