namespace YtFetcher.Application.Backfill.DTO.Result;

/// <summary>
/// One normalized uploads-playlist entry. <see cref="YoutubeVideoId"/> is blank only for a malformed
/// provider item, and <see cref="PublishedAt"/> is <c>null</c> when the provider did not report a
/// usable timestamp; both are discarded by the use case.
/// </summary>
public sealed record UploadsPageItemResult(
    string YoutubeVideoId,
    string Title,
    string? ThumbnailUrl,
    DateTimeOffset? PublishedAt);
