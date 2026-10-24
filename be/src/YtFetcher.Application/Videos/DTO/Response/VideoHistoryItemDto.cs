using YtFetcher.Core.Videos;

namespace YtFetcher.Application.Videos.DTO.Response;

/// <summary>
/// One captured statistics point of a video's history. Values are raw; timestamp and number formatting belong
/// to the client.
/// </summary>
public sealed record VideoHistoryItemDto(
    DateTimeOffset CapturedAt,
    long ViewCount,
    long LikeCount)
{
    public static VideoHistoryItemDto ToDto(VideoSnapshot snapshot) => new(
        snapshot.CapturedAt,
        snapshot.ViewCount,
        snapshot.LikeCount);
}
