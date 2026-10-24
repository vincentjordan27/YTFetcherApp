using YtFetcher.Application.Videos.DTO.Request;
using YtFetcher.Core.Videos;

namespace YtFetcher.Application.Abstractions.Repositories;

/// <summary>
/// Persistence boundary for tracked-video reads and the atomic reference mutations. It owns the
/// database joins/transactions and exposes Core entities plus the joined channel fields; it never
/// returns Application HTTP DTOs.
/// </summary>
public interface IVideoRepository
{
    /// <summary>
    /// Fetches one page of active videos joined to their non-deleted channels, ordered by the requested
    /// persisted column with a deterministic <c>videos.id</c> tie-breaker. The channel name and provider
    /// channel id come from that same joined query. Ordering is applied before <c>Skip</c>/<c>Take</c>, so a
    /// page always carries the globally correct order.
    /// </summary>
    Task<IReadOnlyList<(Video Video, string ChannelName, string YoutubeChannelId)>> GetActivePageAsync(
        int page,
        int pageSize,
        string? searchText,
        VideoListSortBy sortBy,
        VideoListSortDirection sortDirection,
        CancellationToken cancellationToken);

    /// <summary>Counts the active videos that match the optional search text.</summary>
    Task<int> CountActiveAsync(string? searchText, CancellationToken cancellationToken);

    /// <summary>
    /// Reads one page of a single video's snapshot history, newest capture first and then id descending, plus
    /// the matching total. Returns <c>Exists = false</c> when the non-deleted video does not exist; soft-deleted
    /// snapshots are always excluded.
    /// </summary>
    Task<(bool Exists, int TotalCount, IReadOnlyList<VideoSnapshot> Snapshots)> GetSnapshotHistoryPageAsync(
        Guid videoId,
        int page,
        int pageSize,
        CancellationToken cancellationToken);

    /// <summary>
    /// Fetches one page of reference videos joined to their non-deleted channels, ordered by the requested
    /// persisted column with a deterministic <c>videos.id</c> tie-breaker. Channel lifecycle is never a filter.
    /// </summary>
    Task<IReadOnlyList<(Video Video, string ChannelName, string YoutubeChannelId)>> GetReferencePageAsync(
        int page,
        int pageSize,
        string? searchText,
        VideoListSortBy sortBy,
        VideoListSortDirection sortDirection,
        CancellationToken cancellationToken);

    /// <summary>Counts the reference videos that match the optional search text.</summary>
    Task<int> CountReferenceAsync(string? searchText, CancellationToken cancellationToken);

    /// <summary>
    /// In one short transaction, locks every requested video row, verifies each id is a non-deleted,
    /// non-reference video with <c>tracking_status = ACTIVE</c>, applies the Core reference
    /// transition to all of them, and commits. Returns <c>false</c> and rolls back everything when
    /// any requested id fails the check.
    /// </summary>
    Task<bool> TryMarkAsReferenceAsync(
        IReadOnlyList<Guid> videoIds,
        Guid actorUserId,
        DateTimeOffset utcNow,
        CancellationToken cancellationToken);

    /// <summary>
    /// In one short transaction, locks the target row, lets Core re-decide the tracking status from the
    /// mature-stop rule, saves, and commits. Returns <c>false</c> and rolls back without mutation when the
    /// row is missing, soft deleted, or no longer a reference video.
    /// </summary>
    Task<bool> TryRemoveReferenceAsync(
        Guid videoId,
        Guid actorUserId,
        DateTimeOffset utcNow,
        CancellationToken cancellationToken);
}
