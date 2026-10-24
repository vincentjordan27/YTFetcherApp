using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using YtFetcher.Application.Abstractions.Repositories;
using YtFetcher.Application.Videos.DTO.Result;
using YtFetcher.Core.Channels;
using YtFetcher.Core.Users;
using YtFetcher.Infrastructure.Persistence;

namespace YtFetcher.Infrastructure.Persistence.Repositories;

/// <summary>
/// PostgreSQL persistence for M6 new-upload monitoring. The candidate read is a plain soft-delete-filtered
/// channel query; the per-channel write locks the daily ledger row and the approved channel inside one short
/// transaction, re-reads the known video ids, and inserts through <c>ON CONFLICT DO NOTHING</c> so a race with
/// backfill can never create a duplicate video or a second initial snapshot.
/// </summary>
public sealed class NewUploadMonitoringRepository(
    YtFetcherDbContext dbContext,
    TimeProvider timeProvider) : INewUploadMonitoringRepository
{
    public async Task<IReadOnlyList<Channel>> GetUploadMonitorCandidatesAsync(
        DateTimeOffset quotaDateStartUtc,
        CancellationToken cancellationToken) =>
        await dbContext.Channels
            .AsNoTracking()
            .Where(channel =>
                !channel.IsDeleted &&
                channel.StatusValue == nameof(ChannelStatus.APPROVED) &&
                (channel.LastUploadCheckAt == null || channel.LastUploadCheckAt < quotaDateStartUtc))
            // Null marker first, then the oldest marker, then id: a partial same-date retry never spends
            // another playlist request on a channel already checked successfully that Pacific date.
            .OrderByDescending(channel => channel.LastUploadCheckAt == null)
            .ThenBy(channel => channel.LastUploadCheckAt)
            .ThenBy(channel => channel.Id)
            .ToListAsync(cancellationToken);

    public async Task<IReadOnlySet<string>> GetKnownYoutubeVideoIdsAsync(
        IReadOnlyCollection<string> youtubeVideoIds,
        CancellationToken cancellationToken)
    {
        if (youtubeVideoIds.Count == 0)
        {
            return new HashSet<string>(StringComparer.Ordinal);
        }

        var ids = youtubeVideoIds.ToArray();

        // Soft-deleted rows deliberately count as known, exactly like M5: a deleted video is never revived and
        // never receives a second initial snapshot.
        var known = await dbContext.Videos
            .AsNoTracking()
            .Where(video => ids.Contains(video.YoutubeVideoId))
            .Select(video => video.YoutubeVideoId)
            .ToListAsync(cancellationToken);

        return known.ToHashSet(StringComparer.Ordinal);
    }

    public async Task<NewUploadCheckPersistResult> PersistUploadCheckAsync(
        NewUploadCheckRequest request,
        CancellationToken cancellationToken)
    {
        var utcNow = timeProvider.GetUtcNow();

        await using var transaction = await dbContext.Database.BeginTransactionAsync(cancellationToken);

        try
        {
            // Owner authority first: the daily ledger row is locked and re-checked so a response already in
            // flight belongs to a run that no longer has write permission. The predicate is exactly quota-date
            // + live token + not completed; it deliberately does not test a bucket exhaustion marker, because a
            // marker is not proof that this owner lost authority.
            var ownedDates = await dbContext.YouTubeDailyQuotaUsages
                .FromSqlInterpolated($"""
                    SELECT * FROM youtube_daily_quota_usage
                    WHERE quota_date = {request.QuotaDate}
                      AND is_deleted = FALSE
                      AND daily_processing_claim_token = {request.DailyProcessClaimToken}
                      AND daily_processing_completed_at IS NULL
                    FOR UPDATE
                    """)
                .ToListAsync(cancellationToken);

            if (ownedDates.Count != 1)
            {
                await transaction.RollbackAsync(CancellationToken.None);
                dbContext.ChangeTracker.Clear();
                return NewUploadCheckPersistResult.OwnershipLost();
            }

            var channel = await dbContext.Channels
                .FromSqlInterpolated(
                    $"SELECT * FROM channels WHERE id = {request.ChannelId} AND is_deleted = FALSE FOR UPDATE")
                .SingleOrDefaultAsync(cancellationToken);

            if (channel is null || channel.Status != ChannelStatus.APPROVED)
            {
                await transaction.RollbackAsync(CancellationToken.None);
                dbContext.ChangeTracker.Clear();
                return NewUploadCheckPersistResult.ChannelUnavailable();
            }

            // Re-read the known ids inside the transaction, because backfill can finish between the monitor's
            // provider call and this save.
            var candidateIds = request.Items
                .Select(item => item.YoutubeVideoId)
                .Distinct(StringComparer.Ordinal)
                .ToArray();

            var knownIds = candidateIds.Length == 0
                ? new HashSet<string>(StringComparer.Ordinal)
                : (await dbContext.Videos
                        .Where(video => candidateIds.Contains(video.YoutubeVideoId))
                        .Select(video => video.YoutubeVideoId)
                        .ToListAsync(cancellationToken))
                    .ToHashSet(StringComparer.Ordinal);

            var insertedCount = 0;

            foreach (var item in request.Items)
            {
                if (knownIds.Contains(item.YoutubeVideoId))
                {
                    continue;
                }

                // Set-based conflict avoidance: if another flow committed the same video id first, this insert
                // inserts nothing and returns no id, so neither a duplicate video nor a snapshot is written.
                var insertedIds = await dbContext.Database
                    .SqlQuery<Guid>($"""
                        INSERT INTO videos (
                            id,
                            youtube_video_id,
                            channel_id,
                            title,
                            thumbnail_url,
                            published_at,
                            duration_millis,
                            current_views,
                            current_likes,
                            is_reference,
                            tracking_status,
                            consecutive_failures,
                            last_stats_fetched_at,
                            availability_status,
                            is_deleted,
                            created_by,
                            modified_by,
                            created_at,
                            modified_at)
                        VALUES (
                            gen_random_uuid(),
                            {item.YoutubeVideoId},
                            {channel.Id},
                            {item.Title},
                            CAST({item.ThumbnailUrl} AS text),
                            {item.PublishedAt},
                            {item.DurationMillis},
                            {item.ViewCount},
                            {item.LikeCount},
                            FALSE,
                            'ACTIVE',
                            0,
                            {utcNow},
                            'AVAILABLE',
                            FALSE,
                            {User.SystemUserId},
                            {User.SystemUserId},
                            {utcNow},
                            {utcNow})
                        ON CONFLICT (youtube_video_id) DO NOTHING
                        RETURNING id AS "Value"
                        """)
                    .ToListAsync(cancellationToken);

                if (insertedIds.Count != 1)
                {
                    continue;
                }

                // Parent video first, then its one initial snapshot, both inside this transaction.
                await dbContext.Database.ExecuteSqlInterpolatedAsync(
                    $"""
                    INSERT INTO video_snapshots (
                        video_id,
                        captured_at,
                        view_count,
                        like_count,
                        is_deleted,
                        created_by,
                        modified_by,
                        created_at,
                        modified_at)
                    VALUES (
                        {insertedIds[0]},
                        {utcNow},
                        {item.ViewCount},
                        {item.LikeCount},
                        FALSE,
                        {User.SystemUserId},
                        {User.SystemUserId},
                        {utcNow},
                        {utcNow})
                    """,
                    cancellationToken);

                insertedCount++;
            }

            if (!string.IsNullOrWhiteSpace(request.UploadsPlaylistId))
            {
                channel.UploadsPlaylistId = request.UploadsPlaylistId;
            }

            // The marker advances only after the page has fully persisted, in the same transaction.
            channel.RecordUploadCheckSuccess(request.PhaseStartedAtUtc);
            channel.ModifiedByUserId = User.SystemUserId;

            await dbContext.SaveChangesAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);

            return new NewUploadCheckPersistResult(
                OwnershipRetained: true,
                ChannelStillEligible: true,
                insertedCount,
                SkippedKnownCount: request.Items.Count - insertedCount);
        }
        catch
        {
            await RollbackAndClearAsync(transaction);
            throw;
        }
    }

    private async Task RollbackAndClearAsync(IDbContextTransaction transaction)
    {
        await transaction.RollbackAsync(CancellationToken.None);
        dbContext.ChangeTracker.Clear();
    }
}
