using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using YtFetcher.Application.Abstractions.Repositories;
using YtFetcher.Application.Backfill.DTO.Result;
using YtFetcher.Core.BackfillRuns;
using YtFetcher.Core.Channels;
using YtFetcher.Core.Users;
using YtFetcher.Core.Videos;
using YtFetcher.Infrastructure.Persistence;

namespace YtFetcher.Infrastructure.Persistence.Repositories;

/// <summary>
/// Cross-table backfill transaction boundary. Every mutating method locks the channel row with
/// <c>FOR UPDATE</c> inside one PostgreSQL transaction and verifies claim ownership before writing.
/// </summary>
public sealed class BackfillRepository(
    YtFetcherDbContext dbContext,
    TimeProvider timeProvider) : IBackfillRepository
{
    public async Task<Channel?> TryClaimPageAsync(
        Guid channelId,
        Guid claimToken,
        DateOnly quotaDate,
        BackfillTriggerSource triggerSource,
        DateTimeOffset utcNow,
        CancellationToken cancellationToken)
    {
        await using var transaction = await dbContext.Database.BeginTransactionAsync(cancellationToken);

        try
        {
            var channel = await LockChannelAsync(channelId, cancellationToken);
            if (channel is null)
            {
                await transaction.RollbackAsync(CancellationToken.None);
                return null;
            }

            // A page-one attempt whose reservation is missing or from an earlier Pacific date reclaims a fresh
            // paired reservation inside this transaction, before any claim or run row exists. A refusal writes
            // nothing and leaves the channel PENDING for a later date. This is the rollover path: a first page
            // approved just before midnight, or left unfinished by an earlier failure, must not become a
            // technical failure when its Hangfire job runs after midnight.
            if (channel.BackfillPagesCompleted == 0 && channel.FirstPageQuotaDate != quotaDate)
            {
                var reserved = await TryReserveRolloverFirstPageAsync(quotaDate, utcNow, cancellationToken);
                if (!reserved)
                {
                    await transaction.RollbackAsync(CancellationToken.None);
                    return null;
                }

                channel.FirstPageQuotaDate = quotaDate;
            }

            if (!channel.TryClaimPage(claimToken, utcNow))
            {
                await transaction.RollbackAsync(CancellationToken.None);
                return null;
            }

            channel.ModifiedByUserId = User.SystemUserId;

            // The attempt number is the durable progress plus one, so a retry of a failed page keeps the number
            // of the page it resumes instead of restarting at page one.
            var run = ChannelBackfillRun.Start(
                channelId,
                claimToken,
                triggerSource,
                channel.BackfillPagesCompleted + 1,
                utcNow);
            run.CreatedByUserId = User.SystemUserId;
            run.ModifiedByUserId = User.SystemUserId;
            dbContext.ChannelBackfillRuns.Add(run);

            await dbContext.SaveChangesAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);

            return channel;
        }
        catch
        {
            await RollbackAndClearAsync(transaction);
            throw;
        }
    }

    /// <summary>
    /// Claims one paired first-page reservation for <paramref name="quotaDate"/> in the enclosing transaction.
    /// The single conditional update requires a ready window, both buckets not already exhausted, and a free
    /// attempt slot; it increments the three counters together. A rollback of the caller's transaction removes
    /// the increment. The InMemory provider used by host/unit tests cannot run a conditional update, so it uses
    /// the equivalent tracked-entity path.
    /// </summary>
    private async Task<bool> TryReserveRolloverFirstPageAsync(
        DateOnly quotaDate,
        DateTimeOffset utcNow,
        CancellationToken cancellationToken)
    {
        if (!dbContext.Database.IsRelational())
        {
            var tracked = await dbContext.YouTubeDailyQuotaUsages
                .FirstOrDefaultAsync(usage => usage.QuotaDate == quotaDate && !usage.IsDeleted, cancellationToken);

            if (tracked is null ||
                !tracked.IsFirstPageWindowReady ||
                tracked.IsQueriesQuotaExhausted ||
                tracked.IsVideoBatchStatsQuotaExhausted ||
                tracked.FirstPageAttemptUsed >= tracked.FirstPageAttemptLimit)
            {
                return false;
            }

            tracked.FirstPageAttemptUsed += 1;
            tracked.QueriesUnitsUsed += 1;
            tracked.VideoBatchStatsUnitsUsed += 1;
            tracked.ModifiedAt = utcNow;
            tracked.ModifiedByUserId = User.SystemUserId;
            return true;
        }

        var claimed = await dbContext.YouTubeDailyQuotaUsages
            .Where(usage =>
                usage.QuotaDate == quotaDate &&
                !usage.IsDeleted &&
                usage.FirstPageAttemptLimit > 0 &&
                usage.QueriesQuotaExhaustedAt == null &&
                usage.VideoBatchStatsQuotaExhaustedAt == null &&
                usage.FirstPageAttemptUsed < usage.FirstPageAttemptLimit)
            .ExecuteUpdateAsync(
                setters => setters
                    .SetProperty(usage => usage.FirstPageAttemptUsed, usage => usage.FirstPageAttemptUsed + 1)
                    .SetProperty(usage => usage.QueriesUnitsUsed, usage => usage.QueriesUnitsUsed + 1)
                    .SetProperty(usage => usage.VideoBatchStatsUnitsUsed, usage => usage.VideoBatchStatsUnitsUsed + 1)
                    .SetProperty(usage => usage.ModifiedAt, _ => utcNow)
                    .SetProperty(usage => usage.ModifiedByUserId, User.SystemUserId),
                cancellationToken);

        return claimed == 1;
    }

    public async Task ReleaseClaimAsync(
        Guid channelId,
        Guid claimToken,
        DateTimeOffset utcNow,
        CancellationToken cancellationToken)
    {
        await using var transaction = await dbContext.Database.BeginTransactionAsync(cancellationToken);

        try
        {
            var channel = await LockChannelAsync(channelId, cancellationToken);
            if (channel is null || !channel.TryDeferBackfillForQuota(claimToken, utcNow))
            {
                await transaction.RollbackAsync(CancellationToken.None);
                return;
            }

            var openRun = await FindOpenRunAsync(claimToken, cancellationToken);
            if (openRun?.Abandon(utcNow) == true)
            {
                openRun.ModifiedByUserId = User.SystemUserId;
            }
            channel.ModifiedByUserId = User.SystemUserId;

            await dbContext.SaveChangesAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);
        }
        catch
        {
            await RollbackAndClearAsync(transaction);
            throw;
        }
    }

    public async Task<BackfillPagePersistResult> PersistPageAsync(
        BackfillPageResult page,
        CancellationToken cancellationToken)
    {
        var utcNow = timeProvider.GetUtcNow();

        await using var transaction = await dbContext.Database.BeginTransactionAsync(cancellationToken);

        try
        {
            var channel = await LockChannelAsync(page.ChannelId, cancellationToken)
                ?? throw new InvalidOperationException("The backfilled channel no longer exists.");

            if (channel.BackfillClaimToken != page.ClaimToken)
            {
                throw new InvalidOperationException("The backfill claim is no longer owned by this job.");
            }

            // A frozen cutoff stays authoritative so a multi-day backfill cannot lose older videos.
            var effectiveCutoff = channel.BackfillCutoffAt ?? page.RunStartedAt.AddDays(-Channel.BackfillHorizonDays);

            var uploadItemsById = page.UploadItems
                .Where(item => item.PublishedAt is not null)
                .GroupBy(item => item.YoutubeVideoId, StringComparer.Ordinal)
                .ToDictionary(group => group.Key, group => group.First(), StringComparer.Ordinal);

            var candidateVideoIds = page.StatsItems
                .Select(item => item.YoutubeVideoId)
                .Where(uploadItemsById.ContainsKey)
                .Distinct(StringComparer.Ordinal)
                .ToArray();

            // Soft-deleted videos count as already known: they are never revived and never receive a
            // second initial snapshot.
            var knownVideoIds = await dbContext.Videos
                .Where(video => candidateVideoIds.Contains(video.YoutubeVideoId))
                .Select(video => video.YoutubeVideoId)
                .ToListAsync(cancellationToken);

            var known = knownVideoIds.ToHashSet(StringComparer.Ordinal);

            var videosInserted = 0;
            var videosSkipped = 0;
            var initialSnapshots = new List<VideoSnapshot>();

            foreach (var statsItem in page.StatsItems)
            {
                if (!uploadItemsById.TryGetValue(statsItem.YoutubeVideoId, out var uploadItem) ||
                    uploadItem.PublishedAt is not { } publishedAt ||
                    !Video.IsShortForm(statsItem.DurationMillis) ||
                    publishedAt < effectiveCutoff ||
                    known.Contains(statsItem.YoutubeVideoId))
                {
                    videosSkipped++;
                    continue;
                }

                var video = Video.CreateIngested(
                    statsItem.YoutubeVideoId,
                    channel.Id,
                    uploadItem.Title,
                    uploadItem.ThumbnailUrl,
                    publishedAt,
                    statsItem.DurationMillis,
                    statsItem.ViewCount,
                    statsItem.LikeCount,
                    utcNow);
                video.CreatedByUserId = User.SystemUserId;
                video.ModifiedByUserId = User.SystemUserId;

                dbContext.Videos.Add(video);
                var snapshot = VideoSnapshot.CreateInitial(video, utcNow);
                snapshot.CreatedByUserId = User.SystemUserId;
                snapshot.ModifiedByUserId = User.SystemUserId;
                initialSnapshots.Add(snapshot);
                videosInserted++;
            }

            // The entities deliberately have no EF navigation relationship. Flush parent videos
            // first so the database foreign key accepts their initial snapshots in this transaction.
            if (videosInserted > 0)
            {
                await dbContext.SaveChangesAsync(cancellationToken);
                dbContext.VideoSnapshots.AddRange(initialSnapshots);
            }

            if (!string.IsNullOrWhiteSpace(page.UploadsPlaylistId))
            {
                channel.UploadsPlaylistId = page.UploadsPlaylistId;
            }

            channel.FreezeBackfillCutoff(page.RunStartedAt);

            // One domain mutation advances the committed-page count exactly once, releases the claim,
            // consumes the quota reservation, and either completes the channel or stores the next cursor.
            if (!channel.TryRecordBackfillPageSuccess(
                    page.ClaimToken,
                    page.IsChannelComplete,
                    page.NextPageToken,
                    utcNow))
            {
                throw new InvalidOperationException("The backfill claim is no longer owned by this job.");
            }
            channel.ModifiedByUserId = User.SystemUserId;

            var openRun = await FindOpenRunAsync(page.ClaimToken, cancellationToken);
            if (openRun?.Succeed(utcNow) == true)
            {
                openRun.ModifiedByUserId = User.SystemUserId;
            }

            await dbContext.SaveChangesAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);

            return new BackfillPagePersistResult(
                videosInserted,
                videosSkipped,
                channel.BackfillStatus,
                page.NextPageToken is not null);
        }
        catch
        {
            // A rollback does not clear the context's tracked Added/Modified entities, so the
            // caller's separate failure-state write would retry these failed page writes. Drop them
            // before the exception leaves this method.
            await RollbackAndClearAsync(transaction);
            throw;
        }
    }

    public async Task<BackfillFailureResult?> RecordFailureAsync(
        Guid channelId,
        Guid claimToken,
        string failureReason,
        DateTimeOffset utcNow,
        CancellationToken cancellationToken)
    {
        await using var transaction = await dbContext.Database.BeginTransactionAsync(cancellationToken);

        try
        {
            var channel = await LockChannelAsync(channelId, cancellationToken);
            if (channel is null || !channel.RecordBackfillFailure(claimToken, failureReason, utcNow))
            {
                await transaction.RollbackAsync(CancellationToken.None);
                return null;
            }

            var openRun = await FindOpenRunAsync(claimToken, cancellationToken);
            if (openRun?.Fail(failureReason, channel.BackfillFailureCount, utcNow) == true)
            {
                openRun.ModifiedByUserId = User.SystemUserId;
            }
            channel.ModifiedByUserId = User.SystemUserId;

            await dbContext.SaveChangesAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);

            var reachedFailureLimit = channel.BackfillFailureCount >= Channel.BackfillFailureLimit;
            return new BackfillFailureResult(
                channel.BackfillFailureCount,
                reachedFailureLimit,
                channel.BackfillStatus,
                !reachedFailureLimit && channel.Status == ChannelStatus.APPROVED && !channel.IsDeleted);
        }
        catch
        {
            await RollbackAndClearAsync(transaction);
            throw;
        }
    }

    public async Task<BackfillRecoveryResult> RecoverStaleWorkAsync(
        DateTimeOffset staleBeforeUtc,
        DateTimeOffset utcNow,
        int maximumCount,
        CancellationToken cancellationToken)
    {
        await using var transaction = await dbContext.Database.BeginTransactionAsync(cancellationToken);

        try
        {
        // Stale scheduling rows are re-enqueued; an expired running claim is abandoned and returns to the
        // ordinary candidate path. Both groups are locked with SKIP LOCKED and share one per-pass bound, so
        // two concurrent pump instances can never recover the same row.
        var staleScheduling = await dbContext.Channels
            .FromSqlInterpolated($"""
                SELECT * FROM channels
                WHERE status = 'APPROVED'
                  AND is_deleted = FALSE
                  AND backfill_status IN ('SCHEDULING', 'SCHEDULING_FAILED')
                  AND backfill_scheduling_attempted_at IS NOT NULL
                  AND backfill_scheduling_attempted_at <= {staleBeforeUtc}
                ORDER BY backfill_scheduling_attempted_at, id
                LIMIT {maximumCount}
                FOR UPDATE SKIP LOCKED
                """)
            .ToListAsync(cancellationToken);

        var recoveredChannelIds = new List<Guid>();
        foreach (var channel in staleScheduling)
        {
            if (channel.TryBeginRecoveredScheduling(utcNow))
            {
                channel.ModifiedByUserId = User.SystemUserId;
                recoveredChannelIds.Add(channel.Id);
            }
        }

        var abandonedRunCount = 0;
        var remainingCapacity = maximumCount - staleScheduling.Count;

        if (remainingCapacity > 0)
        {
            var expiredRunning = await dbContext.Channels
                .FromSqlInterpolated($"""
                    SELECT * FROM channels
                    WHERE status = 'APPROVED'
                      AND is_deleted = FALSE
                      AND backfill_status = 'RUNNING'
                      AND (backfill_claim_token IS NULL
                        OR backfill_claimed_at IS NULL
                        OR backfill_claimed_at <= {staleBeforeUtc})
                    ORDER BY backfill_claimed_at NULLS FIRST, id
                    LIMIT {remainingCapacity}
                    FOR UPDATE SKIP LOCKED
                    """)
                .ToListAsync(cancellationToken);

            foreach (var channel in expiredRunning)
            {
                var claimToken = channel.BackfillClaimToken;
                if (!channel.TryAbandonExpiredClaim(utcNow))
                {
                    continue;
                }
                channel.ModifiedByUserId = User.SystemUserId;

                if (claimToken is not { } token)
                {
                    continue;
                }

                var openRun = await FindOpenRunAsync(token, cancellationToken);
                if (openRun is not null && openRun.Abandon(utcNow))
                {
                    openRun.ModifiedByUserId = User.SystemUserId;
                    abandonedRunCount++;
                }
            }
        }

        await dbContext.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);

        return new BackfillRecoveryResult(recoveredChannelIds, abandonedRunCount);
        }
        catch
        {
            await RollbackAndClearAsync(transaction);
            throw;
        }
    }

    public async Task<BackfillDispatchResult> DispatchCandidatesAsync(
        int pageLimit,
        DateTimeOffset utcNow,
        int maximumCount,
        CancellationToken cancellationToken)
    {
        await using var transaction = await dbContext.Database.BeginTransactionAsync(cancellationToken);

        try
        {
        // The pump owns no quota state. It selects due queue work and transitions it; reserving a unit belongs
        // to the page worker, immediately before its statistics call, because only the worker knows whether the
        // page produced usable video IDs. A simple bounded selection is therefore correct again: capacity is
        // not a dispatch concern, and a dispatched job that meets an exhausted bucket defers and keeps its
        // cursor for a later dispatch window.
        var candidates = await dbContext.Channels
            .FromSqlInterpolated($"""
                SELECT * FROM channels
                WHERE status = 'APPROVED'
                  AND is_deleted = FALSE
                  AND backfill_status = 'PENDING'
                  AND backfill_claim_token IS NULL
                  AND backfill_ready_at IS NOT NULL
                  AND backfill_ready_at <= {utcNow}
                  AND ((backfill_pages_completed = 0 AND backfill_page_token IS NULL)
                    OR (backfill_pages_completed > 0 AND backfill_page_token IS NOT NULL))
                  AND ({pageLimit} <= 0 OR backfill_pages_completed < {pageLimit})
                ORDER BY backfill_ready_at, id
                LIMIT {maximumCount}
                FOR UPDATE SKIP LOCKED
                """)
            .ToListAsync(cancellationToken);

        var dispatchedChannelIds = new List<Guid>();

        foreach (var channel in candidates)
        {
            if (channel.TryBeginPumpScheduling(utcNow))
            {
                channel.ModifiedByUserId = User.SystemUserId;
                dispatchedChannelIds.Add(channel.Id);
            }
        }

        await dbContext.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);

        return new BackfillDispatchResult(dispatchedChannelIds);
        }
        catch
        {
            await RollbackAndClearAsync(transaction);
            throw;
        }
    }

    public async Task<bool> DeferForQuotaLimitAsync(
        Guid channelId,
        Guid claimToken,
        QuotaLimitBucket bucket,
        DateTimeOffset utcNow,
        CancellationToken cancellationToken)
    {
        await using var transaction = await dbContext.Database.BeginTransactionAsync(cancellationToken);

        try
        {
            // The channel returns to the ordinary pending queue with its cursor, committed count, cutoff and
            // ready time preserved, and the open run row records the bucket that stopped it. The failure count
            // is deliberately untouched because a quota limit is not an execution failure.
            var channel = await LockChannelAsync(channelId, cancellationToken);
            if (channel is null || !channel.TryDeferBackfillForQuota(claimToken, utcNow))
            {
                await transaction.RollbackAsync(CancellationToken.None);
                return false;
            }

            var openRun = await FindOpenRunAsync(claimToken, cancellationToken);
            if (openRun?.ReachQuotaLimit(bucket, utcNow) == true)
            {
                openRun.ModifiedByUserId = User.SystemUserId;
            }
            channel.ModifiedByUserId = User.SystemUserId;

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

    private async Task RollbackAndClearAsync(IDbContextTransaction transaction)
    {
        await transaction.RollbackAsync(CancellationToken.None);
        dbContext.ChangeTracker.Clear();
    }

    /// <summary>
    /// Finds the one open run row of a claim. The claim token is unique per run, so at most one row is open
    /// and a missing row means the attempt was already closed.
    /// </summary>
    private Task<ChannelBackfillRun?> FindOpenRunAsync(Guid claimToken, CancellationToken cancellationToken) =>
        dbContext.ChannelBackfillRuns
            .FirstOrDefaultAsync(
                run => run.ClaimToken == claimToken && run.StatusValue == nameof(BackfillRunStatus.RUNNING),
                cancellationToken);

    /// <summary>
    /// Locks one non-deleted channel row for the duration of the surrounding transaction, so two
    /// concurrent jobs cannot both claim it or overwrite each other's failure state.
    /// </summary>
    /// <summary>
    /// Locks one non-deleted channel row for the duration of the surrounding transaction, so two
    /// concurrent jobs cannot both claim it or overwrite each other's failure state. The InMemory provider used
    /// by unit/host tests cannot run <c>FOR UPDATE</c>, so it uses the portable predicate; PostgreSQL always
    /// takes the row lock.
    /// </summary>
    private Task<Channel?> LockChannelAsync(Guid channelId, CancellationToken cancellationToken) =>
        (dbContext.Database.IsRelational()
            ? dbContext.Channels.FromSqlInterpolated(
                $"SELECT * FROM channels WHERE id = {channelId} AND is_deleted = FALSE FOR UPDATE")
            : dbContext.Channels.Where(channel => channel.Id == channelId && !channel.IsDeleted))
        .SingleOrDefaultAsync(cancellationToken);
}
