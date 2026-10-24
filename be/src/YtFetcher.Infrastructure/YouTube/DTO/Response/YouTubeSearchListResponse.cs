namespace YtFetcher.Infrastructure.YouTube.DTO.Response;

/// <summary>
/// Root of a YouTube <c>search.list</c> payload. Deserialization-only; never leaves Infrastructure.
/// </summary>
internal sealed class YouTubeSearchListResponse
{
    public string? NextPageToken { get; set; }

    public List<SearchItem>? Items { get; set; }
}

internal sealed class SearchItem
{
    public SearchId? Id { get; set; }

    public SearchSnippet? Snippet { get; set; }
}

internal sealed class SearchId
{
    public string? Kind { get; set; }

    public string? VideoId { get; set; }
}

internal sealed class SearchSnippet
{
    public string? PublishedAt { get; set; }

    public string? ChannelId { get; set; }

    public string? Title { get; set; }

    public Thumbnails? Thumbnails { get; set; }
}

internal sealed class Thumbnails
{
    public ThumbnailInfo? Default { get; set; }

    public ThumbnailInfo? Medium { get; set; }

    public ThumbnailInfo? High { get; set; }
}

internal sealed class ThumbnailInfo
{
    public string? Url { get; set; }
}
