namespace YtFetcher.Infrastructure.YouTube.DTO.Request;

/// <summary>
/// Outbound parameters of one YouTube <c>search.list</c> call. Infrastructure-only.
/// It carries the API key, so it is a class instead of a record: records generate a
/// <c>ToString()</c> that would print the key if the value were ever logged.
/// </summary>
internal sealed class YouTubeSearchListRequest
{
    public required string Keyword { get; init; }

    public string? PageToken { get; init; }

    public required string ApiKey { get; init; }
}
