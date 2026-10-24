namespace YtFetcher.Infrastructure.YouTube.DTO.Response;

/// <summary>
/// Root of a YouTube <c>channels.list</c> payload. Deserialization-only; never leaves Infrastructure.
/// Thumbnail shapes are shared with <see cref="YouTubeSearchListResponse"/> instead of being duplicated.
/// </summary>
internal sealed class YouTubeChannelsListResponse
{
    public List<ChannelItem>? Items { get; set; }
}

internal sealed class ChannelItem
{
    public string? Id { get; set; }

    public ChannelSnippet? Snippet { get; set; }

    public ChannelContentDetails? ContentDetails { get; set; }
}

internal sealed class ChannelSnippet
{
    public string? Title { get; set; }

    public Thumbnails? Thumbnails { get; set; }
}

internal sealed class ChannelContentDetails
{
    public RelatedPlaylists? RelatedPlaylists { get; set; }
}

internal sealed class RelatedPlaylists
{
    public string? Uploads { get; set; }
}
