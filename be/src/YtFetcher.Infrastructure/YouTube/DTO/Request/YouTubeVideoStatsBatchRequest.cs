namespace YtFetcher.Infrastructure.YouTube.DTO.Request;

/// <summary>
/// Outbound parameters of one YouTube <c>videos:batchGetStats</c> call. The dedicated endpoint has
/// its own quota bucket and accepts at most 50 ids. Infrastructure-only, and it carries the API key,
/// so it is a class instead of a record.
/// </summary>
internal sealed class YouTubeVideoStatsBatchRequest
{
    public required IReadOnlyList<string> VideoIds { get; init; }

    public required string ApiKey { get; init; }
}
