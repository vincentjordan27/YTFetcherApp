namespace YtFetcher.Application.Videos.DTO.Result;

/// <summary>
/// One fully matched, short-form new-upload candidate ready to be persisted. The use case only builds this
/// from a playlist item that has a usable publication time and a successful provider statistics row.
/// </summary>
public sealed record NewUploadCheckItem(
    string YoutubeVideoId,
    string Title,
    string? ThumbnailUrl,
    DateTimeOffset PublishedAt,
    long DurationMillis,
    long ViewCount,
    long LikeCount);
