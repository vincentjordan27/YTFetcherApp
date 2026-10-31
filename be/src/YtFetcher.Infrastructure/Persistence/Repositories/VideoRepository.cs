using System.Linq.Expressions;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using Npgsql;
using NpgsqlTypes;
using YtFetcher.Application.Abstractions.Repositories;
using YtFetcher.Application.Videos.DTO.Request;
using YtFetcher.Core.Videos;
using YtFetcher.Infrastructure.Persistence;

namespace YtFetcher.Infrastructure.Persistence.Repositories;

/// <summary>
/// Repository for tracked videos. Every list read is one joined query over videos and their non-deleted
/// channel: the channel name and provider channel id are projected in the same statement that orders and
/// pages the rows, so the returned page is globally correct and needs no second lookup. The reference
/// mutations are short all-or-nothing transactions.
/// </summary>
public sealed class VideoRepository(YtFetcherDbContext dbContext) : IVideoRepository
{
    public Task<IReadOnlyList<(Video Video, string ChannelName, string YoutubeChannelId)>> GetActivePageAsync(
        int page,
        int pageSize,
        string? searchText,
        VideoListSortBy sortBy,
        VideoListSortDirection sortDirection,
        CancellationToken cancellationToken) =>
        GetPageAsync(ActiveVideos(searchText), page, pageSize, sortBy, sortDirection, cancellationToken);

    public Task<int> CountActiveAsync(string? searchText, CancellationToken cancellationToken) =>
        ActiveVideos(searchText).CountAsync(cancellationToken);

    public Task<IReadOnlyList<(Video Video, string ChannelName, string YoutubeChannelId)>> GetReferencePageAsync(
        int page,
        int pageSize,
        string? searchText,
        VideoListSortBy sortBy,
        VideoListSortDirection sortDirection,
        CancellationToken cancellationToken) =>
        GetPageAsync(ReferenceVideos(searchText), page, pageSize, sortBy, sortDirection, cancellationToken);

    public Task<int> CountReferenceAsync(string? searchText, CancellationToken cancellationToken) =>
        ReferenceVideos(searchText).CountAsync(cancellationToken);

    public async Task<(bool Exists, int TotalCount, IReadOnlyList<VideoSnapshot> Snapshots)> GetSnapshotHistoryPageAsync(
        Guid videoId,
        int page,
        int pageSize,
        CancellationToken cancellationToken)
    {
        // History is on demand only and 404s on a missing or soft-deleted video.
        var exists = await dbContext.Videos
            .AsNoTracking()
            .AnyAsync(video => video.Id == videoId && !video.IsDeleted, cancellationToken);

        if (!exists)
        {
            return (false, 0, []);
        }

        var snapshots = dbContext.VideoSnapshots
            .AsNoTracking()
            .Where(snapshot => snapshot.VideoId == videoId && !snapshot.IsDeleted);

        var totalCount = await snapshots.CountAsync(cancellationToken);

        // Deterministic paging: newest capture first, then id descending.
        var items = await snapshots
            .OrderByDescending(snapshot => snapshot.CapturedAt)
            .ThenByDescending(snapshot => snapshot.Id)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync(cancellationToken);

        return (true, totalCount, items);
    }

    public async Task<bool> TryMarkAsReferenceAsync(
        IReadOnlyList<Guid> videoIds,
        Guid actorUserId,
        DateTimeOffset utcNow,
        CancellationToken cancellationToken)
    {
        await using var transaction = await dbContext.Database.BeginTransactionAsync(cancellationToken);

        try
        {
            var videos = await LockVideosAsync(videoIds, cancellationToken);

            // All-or-nothing: every requested id must be a live, non-reference, active video. A single
            // miss rolls back the whole request before any row changes.
            var allEligible = videos.Count == videoIds.Count &&
                videos.All(video =>
                    !video.IsDeleted &&
                    !video.IsReference &&
                    video.TrackingStatus == VideoTrackingStatus.ACTIVE);

            if (!allEligible)
            {
                await transaction.RollbackAsync(CancellationToken.None);
                return false;
            }

            foreach (var video in videos)
            {
                video.MarkAsReference(utcNow, actorUserId);
            }

            await dbContext.SaveChangesAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);
            return true;
        }
        catch
        {
            await RollbackAndClearAsync(transaction);
            throw;
        }
    }

    public async Task<bool> TryRemoveReferenceAsync(
        Guid videoId,
        Guid actorUserId,
        DateTimeOffset utcNow,
        CancellationToken cancellationToken)
    {
        await using var transaction = await dbContext.Database.BeginTransactionAsync(cancellationToken);

        try
        {
            // The row lock makes the reference check and the transition atomic against a concurrent
            // statistics or reference write on the same row.
            var locked = await LockVideosAsync([videoId], cancellationToken);
            var video = locked.Count == 1 ? locked[0] : null;

            // A missing, soft-deleted, or already non-reference row is the readable not-found outcome:
            // Core refuses the transition, so nothing is written.
            if (video is null || video.IsDeleted || !video.RemoveReference(utcNow, actorUserId))
            {
                await transaction.RollbackAsync(CancellationToken.None);
                dbContext.ChangeTracker.Clear();
                return false;
            }

            await dbContext.SaveChangesAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);
            return true;
        }
        catch
        {
            await RollbackAndClearAsync(transaction);
            throw;
        }
    }

    public async Task<bool> TrySetReferenceUsedAsync(
        Guid videoId,
        bool isUsed,
        Guid actorUserId,
        DateTimeOffset utcNow,
        CancellationToken cancellationToken)
    {
        await using var transaction = await dbContext.Database.BeginTransactionAsync(cancellationToken);

        try
        {
            var locked = await LockVideosAsync([videoId], cancellationToken);
            var video = locked.Count == 1 ? locked[0] : null;

            if (video is null || video.IsDeleted || !video.SetReferenceUsed(isUsed, utcNow, actorUserId))
            {
                await transaction.RollbackAsync(CancellationToken.None);
                dbContext.ChangeTracker.Clear();
                return false;
            }

            await dbContext.SaveChangesAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);
            return true;
        }
        catch
        {
            await RollbackAndClearAsync(transaction);
            throw;
        }
    }

    /// <summary>
    /// One joined query per page. The order is applied to the video query first, because every sort key is a
    /// video column: EF then emits one statement whose ORDER BY covers the joined result, and the join only
    /// adds the display channel fields. Paging happens on that same ordered statement.
    /// </summary>
    private async Task<IReadOnlyList<(Video Video, string ChannelName, string YoutubeChannelId)>> GetPageAsync(
        IQueryable<Video> query,
        int page,
        int pageSize,
        VideoListSortBy sortBy,
        VideoListSortDirection sortDirection,
        CancellationToken cancellationToken)
    {
        var rows = await OrderVideos(query, sortBy, sortDirection)
            .Join(
                dbContext.Channels.AsNoTracking().Where(channel => !channel.IsDeleted),
                video => video.ChannelId,
                channel => channel.Id,
                (video, channel) => new { Video = video, ChannelName = channel.Name, channel.YoutubeChannelId })
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync(cancellationToken);

        return rows
            .Select(row => (row.Video, row.ChannelName, row.YoutubeChannelId))
            .ToList();
    }

    private static IOrderedQueryable<Video> OrderVideos(
        IQueryable<Video> videos,
        VideoListSortBy sortBy,
        VideoListSortDirection sortDirection)
    {
        var descending = sortDirection == VideoListSortDirection.Desc;

        // A null last-update timestamp must sort after every real timestamp in BOTH directions, so the
        // null flag is the primary key and the requested direction only orders the non-null group.
        if (sortBy == VideoListSortBy.LastUpdatedAt)
        {
            var byTimestamp = descending
                ? videos.OrderBy(video => video.LastStatsFetchedAt == null)
                    .ThenByDescending(video => video.LastStatsFetchedAt)
                : videos.OrderBy(video => video.LastStatsFetchedAt == null)
                    .ThenBy(video => video.LastStatsFetchedAt);

            return TieBreak(byTimestamp, descending);
        }

        if (sortBy == VideoListSortBy.ReferenceSetAt)
        {
            var byTimestamp = descending
                ? videos.OrderBy(video => video.ReferenceSetAt == null)
                    .ThenByDescending(video => video.ReferenceSetAt)
                : videos.OrderBy(video => video.ReferenceSetAt == null)
                    .ThenBy(video => video.ReferenceSetAt);

            return TieBreak(byTimestamp, descending);
        }

        // ToLower() rather than ToLowerInvariant() because the Npgsql provider cannot translate the
        // invariant form; the same convention the existing search predicate uses.
        var ordered = sortBy switch
        {
            VideoListSortBy.Title => Order(videos, video => video.Title.ToLower(), descending),
            VideoListSortBy.Likes => Order(videos, video => video.CurrentLikes, descending),
            VideoListSortBy.PublishedAt => Order(videos, video => video.PublishedAt, descending),
            _ => Order(videos, video => video.CurrentViews, descending)
        };

        return TieBreak(ordered, descending);
    }

    /// <summary>
    /// The final <c>videos.id</c> key makes every order deterministic so rows cannot drift between pages. It
    /// mirrors the requested direction, which keeps the default views-descending order byte-for-byte the same
    /// as the order this list had before sorting existed.
    /// </summary>
    private static IOrderedQueryable<Video> TieBreak(IOrderedQueryable<Video> ordered, bool descending) =>
        descending
            ? ordered.ThenByDescending(video => video.Id)
            : ordered.ThenBy(video => video.Id);

    private static IOrderedQueryable<Video> Order<TKey>(
        IQueryable<Video> videos,
        Expression<Func<Video, TKey>> keySelector,
        bool descending) =>
        descending ? videos.OrderByDescending(keySelector) : videos.OrderBy(keySelector);

    private IQueryable<Video> ActiveVideos(string? searchText) =>
        VisibleVideos(searchText).Where(video =>
            !video.IsReference &&
            video.TrackingStatusValue == nameof(VideoTrackingStatus.ACTIVE));

    private IQueryable<Video> ReferenceVideos(string? searchText) =>
        VisibleVideos(searchText).Where(video => video.IsReference);

    private IQueryable<Video> VisibleVideos(string? searchText)
    {
        var videos = dbContext.Videos
            .AsNoTracking()
            .Where(video =>
                !video.IsDeleted &&
                dbContext.Channels.Any(channel => channel.Id == video.ChannelId && !channel.IsDeleted));

        if (string.IsNullOrEmpty(searchText))
        {
            return videos;
        }

        // ToLower() is used instead of ToLowerInvariant() because the Npgsql provider cannot
        // translate the invariant form; ChannelSearchQueryTranslationTests guards the same choice.
        var search = searchText.ToLower();

        return videos.Where(video =>
            video.Title.ToLower().Contains(search) ||
            dbContext.Channels.Any(channel =>
                channel.Id == video.ChannelId &&
                !channel.IsDeleted &&
                channel.Name.ToLower().Contains(search)));
    }

    private Task<List<Video>> LockVideosAsync(
        IReadOnlyList<Guid> videoIds,
        CancellationToken cancellationToken)
    {
        var ids = videoIds.ToArray();

        if (dbContext.Database.IsRelational())
        {
            // Explicit uuid[] so the lock query is unambiguous on PostgreSQL. The rows stay tracked,
            // so the following Core transition is persisted by SaveChanges.
            var idsParameter = new NpgsqlParameter("ids", NpgsqlDbType.Array | NpgsqlDbType.Uuid)
            {
                Value = ids
            };

            return dbContext.Videos
                .FromSqlRaw("SELECT * FROM videos WHERE id = ANY(@ids) FOR UPDATE", idsParameter)
                .ToListAsync(cancellationToken);
        }

        // The InMemory provider has no row locks; the portable predicate is used only by tests.
        return dbContext.Videos
            .Where(video => ids.Contains(video.Id))
            .ToListAsync(cancellationToken);
    }

    private async Task RollbackAndClearAsync(IDbContextTransaction transaction)
    {
        await transaction.RollbackAsync(CancellationToken.None);
        dbContext.ChangeTracker.Clear();
    }
}
