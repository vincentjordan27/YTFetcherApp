namespace YtFetcher.Application.Backfill.DTO.Result;

/// <summary>
/// One normalized video statistics row. Counts are never negative, and
/// <see cref="DurationMillis"/> is <c>-1</c> when the provider reported no usable duration.
/// </summary>
public sealed record VideoStatsItemResult(
    string YoutubeVideoId,
    long DurationMillis,
    long ViewCount,
    long LikeCount);
