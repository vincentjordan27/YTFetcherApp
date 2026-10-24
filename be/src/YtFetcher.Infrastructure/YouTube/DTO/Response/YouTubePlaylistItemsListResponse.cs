namespace YtFetcher.Infrastructure.YouTube.DTO.Response;

/// <summary>
/// Root of a YouTube <c>playlistItems.list</c> payload. Deserialization-only; never leaves
/// Infrastructure. The shared <see cref="Thumbnails"/> shape is reused instead of duplicated.
/// </summary>
internal sealed class YouTubePlaylistItemsListResponse
{
    public string? NextPageToken { get; set; }

    public List<PlaylistItem>? Items { get; set; }
}

internal sealed class PlaylistItem
{
    public PlaylistItemContentDetails? ContentDetails { get; set; }

    public PlaylistItemSnippet? Snippet { get; set; }
}

internal sealed class PlaylistItemContentDetails
{
    public string? VideoId { get; set; }
}

internal sealed class PlaylistItemSnippet
{
    public string? PublishedAt { get; set; }

    public string? Title { get; set; }

    public Thumbnails? Thumbnails { get; set; }
}
