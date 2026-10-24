using System.Text.Json;

namespace YtFetcher.Infrastructure.YouTube.DTO.Response;

/// <summary>
/// Root of a YouTube <c>videos:batchGetStats</c> payload. Deserialization-only; never leaves
/// Infrastructure. YouTube reports numeric values as JSON strings, so the numeric members stay
/// <see cref="JsonElement"/> and are read defensively by the client.
/// </summary>
internal sealed class YouTubeVideoStatsBatchResponse
{
    public List<VideoStatsItem>? Items { get; set; }

    public VideoStatsSummary? Summary { get; set; }
}

internal sealed class VideoStatsItem
{
    public string? Id { get; set; }

    public VideoStatsContentDetails? ContentDetails { get; set; }

    public VideoStatsStatistics? Statistics { get; set; }
}

internal sealed class VideoStatsContentDetails
{
    public string? Duration { get; set; }

    public JsonElement? DurationMillis { get; set; }
}

internal sealed class VideoStatsStatistics
{
    public JsonElement? ViewCount { get; set; }

    public JsonElement? LikeCount { get; set; }
}

internal sealed class VideoStatsSummary
{
    public int? RequestedVideoCount { get; set; }

    public int? SucceededVideoCount { get; set; }

    public int? FailedVideoCount { get; set; }
}
