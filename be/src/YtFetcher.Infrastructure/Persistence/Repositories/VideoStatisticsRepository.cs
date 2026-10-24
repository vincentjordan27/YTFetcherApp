using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using YtFetcher.Application.Abstractions.Repositories;
using YtFetcher.Application.Backfill.DTO.Result;
using YtFetcher.Application.Videos.DTO.Result;
using YtFetcher.Core.Users;
using YtFetcher.Core.Videos;
using YtFetcher.Infrastructure.Persistence;

namespace YtFetcher.Infrastructure.Persistence.Repositories;

/// <summary>
/// PostgreSQL durable statistics claims and success persistence for the daily tracked-video phase. Claim
/// selection locks candidate rows with <c>FOR UPDATE SKIP LOCKED</c> and commits before any provider call;
/// success persistence locks the claimed rows again with <c>FOR UPDATE</c> and re-checks ownership before it
/// writes anything, so a stale attempt can never overwrite a later one or clear a newer claim.
/// </summary>
public sealed class VideoStatisticsRepository(YtFetcherDbContext dbContext) : IVideoStatisticsRepository
{
    public async Task<int> ReleaseExpiredClaimsAsync(
        DateTimeOffset claimedBefore,
        DateTimeOffset utcNow,
        CancellationToken cancellationToken)
    {
        // One server-side statement, and only stale claims match: a live attempt keeps its ownership.
        return await dbContext.Videos
            .Where(video =>
                video.StatsClaimToken != null &&
                video.StatsClaimedAt != null &&
                video.StatsClaimedAt < claimedBefore)
            .ExecuteUpdateAsync(
                setters => setters
                    .SetProperty(video => video.StatsClaimToken, (Guid?)null)
                    .SetProperty(video => video.StatsClaimedAt, (DateTimeOffset?)null)
                    .SetProperty(video => video.ModifiedAt, utcNow)
                    .SetProperty(video => video.ModifiedByUserId, User.SystemUserId),
                cancellationToken);
    }

    public async Task<IReadOnlyList<Video>> ClaimDueBatchAsync(
        DateTimeOffset quotaDateStartUtc,
        DateTimeOffset utcNow,
        Guid claimToken,
        int batchSize,
        CancellationToken cancellationToken)
    {
        await using var transaction = await dbContext.Database.BeginTransactionAsync(cancellationToken);

        try
        {
        // SKIP LOCKED is the only way to read and claim without a second daily process waiting on the same
        // rows, and the transaction commits before the provider call, so no row lock ever crosses HTTP.
        var candidates = await dbContext.Videos
            .FromSqlInterpolated($"""
                SELECT * FROM videos
                WHERE is_deleted = FALSE
                  AND is_reference = FALSE
                  AND tracking_status = 'ACTIVE'
                  AND (last_stats_fetched_at IS NULL OR last_stats_fetched_at < {quotaDateStartUtc})
                  AND stats_claim_token IS NULL
                ORDER BY last_stats_fetched_at NULLS FIRST, id
                LIMIT {batchSize}
                FOR UPDATE SKIP LOCKED
                """)
            .ToListAsync(cancellationToken);

        foreach (var video in candidates)
        {
            video.ClaimForStatistics(claimToken, utcNow);
            video.ModifiedAt = utcNow;
            video.ModifiedByUserId = User.SystemUserId;
        }

        await dbContext.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);

        return candidates;
        }
        catch
        {
            await RollbackAndClearAsync(transaction);
            throw;
        }
    }

    public async Task<VideoStatisticsPersistResult> PersistSuccessAsync(
        DateOnly quotaDate,
        Guid dailyProcessClaimToken,
        IReadOnlyList<Guid> claimedVideoIds,
        Guid claimToken,
        IReadOnlyList<VideoStatsItemResult> items,
        DateTimeOffset fetchedAt,
        CancellationToken cancellationToken)
    {
        var videoIds = claimedVideoIds.ToArray();

        await using var transaction = await dbContext.Database.BeginTransactionAsync(cancellationToken);

        try
        {
        // The daily ledger row is the ownership authority for the whole phase, so it is locked and checked first.
        // The predicate is exactly quota-date + live token + not completed. It deliberately does not test a
        // bucket exhaustion marker: a provider response already in flight may have succeeded before another
        // valid actor recorded that marker, and a global marker is not proof that this owner lost authority.
        var ownedDates = await dbContext.YouTubeDailyQuotaUsages
            .FromSqlInterpolated($"""
                SELECT * FROM youtube_daily_quota_usage
                WHERE quota_date = {quotaDate}
                  AND is_deleted = FALSE
                  AND daily_processing_claim_token = {dailyProcessClaimToken}
                  AND daily_processing_completed_at IS NULL
                FOR UPDATE
                """)
            .ToListAsync(cancellationToken);

        if (ownedDates.Count != 1)
        {
            // The daily owner changed: a response already in flight belongs to a run that no longer has write
            // permission, so it is discarded without touching videos, snapshots, claims, or ledger fields.
            await transaction.RollbackAsync(CancellationToken.None);
            dbContext.ChangeTracker.Clear();
            return VideoStatisticsPersistResult.OwnershipLost();
        }

        // Lock exactly the rows this attempt believes it owns, then verify video ownership in the same transaction.
        var claimed = await dbContext.Videos
            .FromSqlInterpolated($"""
                SELECT * FROM videos
                WHERE id = ANY({videoIds})
                  AND stats_claim_token = {claimToken}
                FOR UPDATE
                """)
            .ToListAsync(cancellationToken);

        if (claimed.Count != videoIds.Length)
        {
            // Video ownership was lost: roll the whole batch back rather than partially persist counts/snapshots
            // or clear a claim a newer attempt now owns.
            await transaction.RollbackAsync(CancellationToken.None);
            dbContext.ChangeTracker.Clear();
            return VideoStatisticsPersistResult.OwnershipLost();
        }

        var returned = items.ToDictionary(item => item.YoutubeVideoId, StringComparer.Ordinal);
        var snapshots = new List<VideoSnapshot>();
        var updated = 0;
        var unavailable = 0;

        // Parent updates are applied before the child inserts below, and both stay inside this transaction.
        foreach (var video in claimed)
        {
            if (returned.TryGetValue(video.YoutubeVideoId, out var item))
            {
                video.ApplyStatistics(item.ViewCount, item.LikeCount, fetchedAt);
                video.ModifiedByUserId = User.SystemUserId;
                var snapshot = VideoSnapshot.CreateInitial(video, fetchedAt);
                snapshot.CreatedByUserId = User.SystemUserId;
                snapshot.ModifiedByUserId = User.SystemUserId;
                snapshots.Add(snapshot);
                updated++;
            }
            else
            {
                // Requested but absent from a successful response: not retrievable, and never zero statistics.
                video.MarkUnavailable(fetchedAt);
                video.ModifiedByUserId = User.SystemUserId;
                unavailable++;
            }
        }

        dbContext.VideoSnapshots.AddRange(snapshots);

        // One SaveChanges means one transaction, so counts, availability transitions, snapshots, and the claim
        // releases are all-or-nothing.
        await dbContext.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);

        return new VideoStatisticsPersistResult(true, updated, unavailable);
        }
        catch
        {
            await RollbackAndClearAsync(transaction);
            throw;
        }
    }

    public async Task ReleaseClaimsAsync(
        IReadOnlyList<Guid> claimedVideoIds,
        Guid claimToken,
        DateTimeOffset utcNow,
        CancellationToken cancellationToken)
    {
        var videoIds = claimedVideoIds.ToArray();

        // Token-matched bulk release; modified_at is written so a release is a visible modification.
        await dbContext.Videos
            .Where(video => videoIds.Contains(video.Id) && video.StatsClaimToken == claimToken)
            .ExecuteUpdateAsync(
                setters => setters
                    .SetProperty(video => video.StatsClaimToken, (Guid?)null)
                    .SetProperty(video => video.StatsClaimedAt, (DateTimeOffset?)null)
                    .SetProperty(video => video.ModifiedAt, utcNow)
                    .SetProperty(video => video.ModifiedByUserId, User.SystemUserId),
                cancellationToken);
    }

    private async Task RollbackAndClearAsync(IDbContextTransaction transaction)
    {
        await transaction.RollbackAsync(CancellationToken.None);
        dbContext.ChangeTracker.Clear();
    }
}
