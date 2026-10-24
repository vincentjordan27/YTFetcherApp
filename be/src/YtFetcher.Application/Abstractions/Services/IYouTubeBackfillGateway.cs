using YtFetcher.Application.Backfill.DTO.Result;

namespace YtFetcher.Application.Abstractions.Services;

/// <summary>
/// YouTube backfill gateway boundary. Implemented in Infrastructure, which owns the raw provider
/// shapes and maps them into the normalized Application results before returning.
/// </summary>
public interface IYouTubeBackfillGateway
{
    Task<UploadsPlaylistResult> ResolveUploadsPlaylistAsync(
        string youtubeChannelId,
        CancellationToken cancellationToken);

    /// <summary>
    /// Reads exactly one uploads page. <paramref name="pageToken"/> is the durable continuation cursor:
    /// <c>null</c> asks for the newest (first) page, and a non-null value resumes from that cursor. The
    /// returned <see cref="UploadsPageResult.NextPageToken"/> is the cursor for the following page and is
    /// <c>null</c> when the playlist has no more pages.
    /// </summary>
    Task<UploadsPageResult> GetUploadsPageAsync(
        string uploadsPlaylistId,
        string? pageToken,
        CancellationToken cancellationToken);

    Task<VideoStatsBatchResult> GetVideoStatsAsync(
        IReadOnlyCollection<string> youtubeVideoIds,
        CancellationToken cancellationToken);
}
