using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using YtFetcher.Application.Abstractions.Repositories;
using YtFetcher.Application.Channels.DTO.Result;
using YtFetcher.Core.Channels;
using YtFetcher.Core.Discovery;
using YtFetcher.Core.Users;
using YtFetcher.Infrastructure.Persistence;

namespace YtFetcher.Infrastructure.Persistence.Repositories;

public sealed class ChannelRepository(YtFetcherDbContext dbContext) : IChannelRepository
{
    public async Task<Channel?> GetActiveAsync(Guid channelId, CancellationToken cancellationToken) =>
        await dbContext.Channels
            .AsNoTracking()
            .SingleOrDefaultAsync(
                channel => channel.Id == channelId && !channel.IsDeleted,
                cancellationToken);

    public async Task<Channel?> GetActiveForUpdateAsync(Guid channelId, CancellationToken cancellationToken) =>
        await dbContext.Channels
            .SingleOrDefaultAsync(
                channel => channel.Id == channelId && !channel.IsDeleted,
                cancellationToken);

    public async Task<IReadOnlyList<Channel>> GetPendingPageAsync(
        int page,
        int pageSize,
        string? searchText,
        CancellationToken cancellationToken) =>
        await ChannelsByStatus(nameof(ChannelStatus.PENDING), searchText)
            .OrderByDescending(channel => channel.DiscoveredAt)
            .ThenByDescending(channel => channel.Id)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync(cancellationToken);

    public Task<int> CountPendingAsync(string? searchText, CancellationToken cancellationToken) =>
        ChannelsByStatus(nameof(ChannelStatus.PENDING), searchText).CountAsync(cancellationToken);

    public async Task<IReadOnlyList<Channel>> GetReviewPageAsync(
        ChannelStatus status,
        int page,
        int pageSize,
        string? searchText,
        CancellationToken cancellationToken)
    {
        var ordered = status switch
        {
            ChannelStatus.APPROVED => ReviewChannels(status, searchText)
                .OrderByDescending(channel => channel.ApprovedAt)
                .ThenByDescending(channel => channel.Id),
            _ => ReviewChannels(status, searchText)
                .OrderByDescending(channel => channel.RejectedAt)
                .ThenByDescending(channel => channel.Id)
        };

        return await ordered
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync(cancellationToken);
    }

    public Task<int> CountReviewChannelsAsync(
        ChannelStatus status,
        string? searchText,
        CancellationToken cancellationToken) =>
        ReviewChannels(status, searchText).CountAsync(cancellationToken);

    public async Task<IReadOnlyDictionary<ChannelStatus, int>> CountChannelsByStatusAsync(
        CancellationToken cancellationToken)
    {
        var counts = await dbContext.Channels
            .AsNoTracking()
            .Where(channel => !channel.IsDeleted)
            .GroupBy(channel => channel.StatusValue)
            .Select(group => new { StatusValue = group.Key, Count = group.Count() })
            .ToListAsync(cancellationToken);

        return counts.ToDictionary(
            entry => Enum.Parse<ChannelStatus>(entry.StatusValue, ignoreCase: false),
            entry => entry.Count);
    }

    public async Task<IReadOnlyDictionary<Guid, int>> CountEvidenceAsync(
        IReadOnlyCollection<Guid> channelIds,
        CancellationToken cancellationToken)
    {
        if (channelIds.Count == 0)
        {
            return new Dictionary<Guid, int>();
        }

        var counts = await dbContext.ChannelDiscoveries
            .AsNoTracking()
            .Where(discovery => channelIds.Contains(discovery.ChannelId) && !discovery.IsDeleted)
            .GroupBy(discovery => discovery.ChannelId)
            .Select(group => new { ChannelId = group.Key, Count = group.Count() })
            .ToListAsync(cancellationToken);

        return counts.ToDictionary(entry => entry.ChannelId, entry => entry.Count);
    }

    public async Task<IReadOnlyList<ChannelDiscovery>> GetEvidenceAsync(
        Guid channelId,
        int limit,
        CancellationToken cancellationToken) =>
        await dbContext.ChannelDiscoveries
            .AsNoTracking()
            .Where(discovery => discovery.ChannelId == channelId && !discovery.IsDeleted)
            .OrderByDescending(discovery => discovery.VideoPublishedAt)
            .ThenByDescending(discovery => discovery.Id)
            .Take(limit)
            .ToListAsync(cancellationToken);

    public async Task<IReadOnlyDictionary<Guid, DiscoveryKeyword>> GetDiscoveryKeywordsByIdsAsync(
        IReadOnlyCollection<Guid> keywordIds,
        CancellationToken cancellationToken)
    {
        if (keywordIds.Count == 0)
        {
            return new Dictionary<Guid, DiscoveryKeyword>();
        }

        var keywords = await dbContext.DiscoveryKeywords
            .AsNoTracking()
            .Where(keyword => keywordIds.Contains(keyword.Id) && keyword.IsActive && !keyword.IsDeleted)
            .ToListAsync(cancellationToken);

        return keywords.ToDictionary(keyword => keyword.Id);
    }

    public Task SaveChangesAsync(CancellationToken cancellationToken) =>
        dbContext.SaveChangesAsync(cancellationToken);

    public async Task<ManualChannelCreateResult> CreateApprovedForSchedulingAsync(
        Channel channel,
        DateOnly quotaDate,
        CancellationToken cancellationToken)
    {
        await using var transaction = await dbContext.Database.BeginTransactionAsync(cancellationToken);

        try
        {
            // The durable read is the common duplicate path. It includes soft-deleted rows because the
            // manual-add policy reports those as a distinct conflict.
            var existing = await FindByYoutubeChannelIdAsync(channel.YoutubeChannelId, cancellationToken);
            if (existing is not null)
            {
                await transaction.RollbackAsync(CancellationToken.None);
                return ManualChannelCreateResult.Duplicate(existing);
            }

            // The paired first-page slot is claimed in the same transaction as the insert, so a refusal
            // leaves no channel and no consumed bucket unit behind.
            var slot = await TryClaimFirstPageSlotsAsync(quotaDate, 1, channel.ModifiedAt, cancellationToken);
            if (slot != ChannelApprovalStatus.Succeeded)
            {
                await transaction.RollbackAsync(CancellationToken.None);
                dbContext.ChangeTracker.Clear();
                return ManualChannelCreateResult.Refused(slot);
            }

            channel.FirstPageQuotaDate = quotaDate;
            dbContext.Channels.Add(channel);

            await dbContext.SaveChangesAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);
            return ManualChannelCreateResult.Created(channel);
        }
        catch (DbUpdateException) when (dbContext.Database.IsRelational())
        {
            // A concurrent manual add won the unique youtube_channel_id constraint after our read.
            // Drop the losing insert, roll back (which also releases the slot claim), and return the
            // durable row it collided with.
            dbContext.Entry(channel).State = EntityState.Detached;
            await transaction.RollbackAsync(CancellationToken.None);
            dbContext.ChangeTracker.Clear();

            var durable = await FindByYoutubeChannelIdAsync(channel.YoutubeChannelId, cancellationToken);
            if (durable is null)
            {
                throw;
            }

            return ManualChannelCreateResult.Duplicate(durable);
        }
        catch
        {
            await RollbackAndClearAsync(transaction);
            throw;
        }
    }

    public async Task<ChannelApprovalResult> TryApproveForSchedulingAsync(
        Guid channelId,
        DateOnly quotaDate,
        Guid approvedByUserId,
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
                return ChannelApprovalResult.Refused(ChannelApprovalStatus.NotFound);
            }

            if (channel.Status != ChannelStatus.PENDING)
            {
                await transaction.RollbackAsync(CancellationToken.None);
                return ChannelApprovalResult.Refused(ChannelApprovalStatus.NotPending);
            }

            var slot = await TryClaimFirstPageSlotsAsync(quotaDate, 1, utcNow, cancellationToken);
            if (slot != ChannelApprovalStatus.Succeeded)
            {
                await transaction.RollbackAsync(CancellationToken.None);
                dbContext.ChangeTracker.Clear();
                return ChannelApprovalResult.Refused(slot);
            }

            if (!channel.TryBeginSchedulingAfterApproval(utcNow, approvedByUserId))
            {
                await transaction.RollbackAsync(CancellationToken.None);
                dbContext.ChangeTracker.Clear();
                return ChannelApprovalResult.Refused(ChannelApprovalStatus.NotPending);
            }

            channel.FirstPageQuotaDate = quotaDate;

            await dbContext.SaveChangesAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);

            return ChannelApprovalResult.Succeeded(channel);
        }
        catch
        {
            await RollbackAndClearAsync(transaction);
            throw;
        }
    }

    public async Task<ChannelBatchMutationResult> TryApproveManyForSchedulingAsync(
        IReadOnlyList<Guid> channelIds,
        DateOnly quotaDate,
        Guid approvedByUserId,
        DateTimeOffset utcNow,
        CancellationToken cancellationToken)
    {
        await using var transaction = await dbContext.Database.BeginTransactionAsync(cancellationToken);

        try
        {
            var channels = await LockChannelsAsync(channelIds, cancellationToken);
            if (channels.Count != channelIds.Count)
            {
                await RollbackAndClearAsync(transaction);
                return ChannelBatchMutationResult.Refused(ChannelBatchMutationStatus.NotFound);
            }

            if (channels.Any(channel => channel.Status != ChannelStatus.PENDING))
            {
                await RollbackAndClearAsync(transaction);
                return ChannelBatchMutationResult.Refused(ChannelBatchMutationStatus.InvalidStatus);
            }

            var slot = await TryClaimFirstPageSlotsAsync(quotaDate, channelIds.Count, utcNow, cancellationToken);
            if (slot != ChannelApprovalStatus.Succeeded)
            {
                await RollbackAndClearAsync(transaction);
                return ChannelBatchMutationResult.Refused(
                    slot == ChannelApprovalStatus.WindowNotReady
                        ? ChannelBatchMutationStatus.WindowNotReady
                        : ChannelBatchMutationStatus.LimitReached);
            }

            foreach (var channel in channels)
            {
                if (!channel.TryBeginSchedulingAfterApproval(utcNow, approvedByUserId))
                {
                    await RollbackAndClearAsync(transaction);
                    return ChannelBatchMutationResult.Refused(ChannelBatchMutationStatus.InvalidStatus);
                }

                channel.FirstPageQuotaDate = quotaDate;
            }

            await dbContext.SaveChangesAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);
            return ChannelBatchMutationResult.Succeeded(channels);
        }
        catch
        {
            await RollbackAndClearAsync(transaction);
            throw;
        }
    }

    public async Task<ChannelBatchMutationResult> TryRejectManyAsync(
        IReadOnlyList<Guid> channelIds,
        Guid rejectedByUserId,
        string reason,
        DateTimeOffset utcNow,
        CancellationToken cancellationToken)
    {
        await using var transaction = await dbContext.Database.BeginTransactionAsync(cancellationToken);

        try
        {
            var channels = await LockChannelsAsync(channelIds, cancellationToken);
            if (channels.Count != channelIds.Count)
            {
                await RollbackAndClearAsync(transaction);
                return ChannelBatchMutationResult.Refused(ChannelBatchMutationStatus.NotFound);
            }

            if (channels.Any(channel => channel.Status is not (ChannelStatus.PENDING or ChannelStatus.APPROVED)))
            {
                await RollbackAndClearAsync(transaction);
                return ChannelBatchMutationResult.Refused(ChannelBatchMutationStatus.InvalidStatus);
            }

            var previousStatuses = channels.ToDictionary(channel => channel.Id, channel => channel.Status);

            foreach (var channel in channels)
            {
                if (!channel.Reject(utcNow, rejectedByUserId, reason))
                {
                    await RollbackAndClearAsync(transaction);
                    return ChannelBatchMutationResult.Refused(ChannelBatchMutationStatus.InvalidStatus);
                }
            }

            await dbContext.SaveChangesAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);
            return ChannelBatchMutationResult.Succeeded(channels, previousStatuses);
        }
        catch
        {
            await RollbackAndClearAsync(transaction);
            throw;
        }
    }

    /// <summary>
    /// Claims the paired first-page slot for one Pacific date in one atomic conditional update: it consumes
    /// one Queries unit, one Video Batch Stats unit, and one configured attempt slot. This is the only place a
    /// first-page pair is reserved, and the reservation is never refunded. A known exhaustion marker on either
    /// bucket refuses the claim, because that bucket must not make another provider call this Pacific date, and
    /// the refusal writes no counter. Returns <see cref="ChannelApprovalStatus.WindowNotReady"/> before Phase 0
    /// has committed the date's limit and <see cref="ChannelApprovalStatus.LimitReached"/> for either a full
    /// window or a known bucket exhaustion.
    /// </summary>
    private async Task<ChannelApprovalStatus> TryClaimFirstPageSlotsAsync(
        DateOnly quotaDate,
        int count,
        DateTimeOffset utcNow,
        CancellationToken cancellationToken)
    {
        // The InMemory provider used by API host tests cannot execute a conditional UPDATE, so it uses the
        // tracked-entity path. PostgreSQL uses one atomic conditional update; both enforce the same predicate.
        if (!dbContext.Database.IsRelational())
        {
            var tracked = await dbContext.YouTubeDailyQuotaUsages
                .FirstOrDefaultAsync(usage => usage.QuotaDate == quotaDate && !usage.IsDeleted, cancellationToken);

            if (tracked is null || !tracked.IsFirstPageWindowReady)
            {
                return ChannelApprovalStatus.WindowNotReady;
            }

            if (tracked.IsQueriesQuotaExhausted ||
                tracked.IsVideoBatchStatsQuotaExhausted ||
                tracked.FirstPageAttemptUsed + count > tracked.FirstPageAttemptLimit)
            {
                return ChannelApprovalStatus.LimitReached;
            }

            tracked.FirstPageAttemptUsed += count;
            tracked.QueriesUnitsUsed += count;
            tracked.VideoBatchStatsUnitsUsed += count;
            tracked.ModifiedAt = utcNow;

            return ChannelApprovalStatus.Succeeded;
        }

        var claimed = await dbContext.YouTubeDailyQuotaUsages
            .Where(usage =>
                usage.QuotaDate == quotaDate &&
                !usage.IsDeleted &&
                usage.FirstPageAttemptLimit > 0 &&
                usage.QueriesQuotaExhaustedAt == null &&
                usage.VideoBatchStatsQuotaExhaustedAt == null &&
                usage.FirstPageAttemptUsed + count <= usage.FirstPageAttemptLimit)
            .ExecuteUpdateAsync(
                setters => setters
                    .SetProperty(usage => usage.FirstPageAttemptUsed, usage => usage.FirstPageAttemptUsed + count)
                    .SetProperty(usage => usage.QueriesUnitsUsed, usage => usage.QueriesUnitsUsed + count)
                    .SetProperty(usage => usage.VideoBatchStatsUnitsUsed, usage => usage.VideoBatchStatsUnitsUsed + count)
                    .SetProperty(usage => usage.ModifiedAt, _ => utcNow),
                cancellationToken);

        if (claimed == 1)
        {
            return ChannelApprovalStatus.Succeeded;
        }

        var ledger = await dbContext.YouTubeDailyQuotaUsages
            .AsNoTracking()
            .SingleOrDefaultAsync(
                usage => usage.QuotaDate == quotaDate && !usage.IsDeleted,
                cancellationToken);

        return ledger is null || !ledger.IsFirstPageWindowReady
            ? ChannelApprovalStatus.WindowNotReady
            : ChannelApprovalStatus.LimitReached;
    }

    public async Task<Channel?> TryFailSchedulingAsync(
        Guid channelId,
        string safeReason,
        DateTimeOffset utcNow,
        CancellationToken cancellationToken)
    {
        await using var transaction = await dbContext.Database.BeginTransactionAsync(cancellationToken);

        try
        {
            var channel = await LockChannelAsync(channelId, cancellationToken);
            if (channel is null || !channel.TryFailScheduling(safeReason, utcNow))
            {
                await transaction.RollbackAsync(CancellationToken.None);
                return null;
            }

            channel.ModifiedByUserId = User.SystemUserId;

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
    /// Reads the row that owns a canonical YouTube channel ID, including soft-deleted rows because
    /// the manual-add duplicate policy distinguishes them. The unique constraint guarantees at most one.
    /// </summary>
    private Task<Channel?> FindByYoutubeChannelIdAsync(
        string youtubeChannelId,
        CancellationToken cancellationToken) =>
        dbContext.Channels.SingleOrDefaultAsync(
            channel => channel.YoutubeChannelId == youtubeChannelId,
            cancellationToken);

    /// <summary>
    /// Locks one non-deleted channel row for the duration of the surrounding transaction, so two
    /// concurrent scheduling transitions cannot both change the same row. The row lock is
    /// PostgreSQL-specific, so the portable predicate is used when the test InMemory provider is in
    /// use; the real concurrency proof lives in the PostgreSQL-only suite.
    /// </summary>
    private Task<Channel?> LockChannelAsync(Guid channelId, CancellationToken cancellationToken) =>
        (dbContext.Database.IsRelational()
            ? dbContext.Channels.FromSqlInterpolated(
                $"SELECT * FROM channels WHERE id = {channelId} AND is_deleted = FALSE FOR UPDATE")
            : dbContext.Channels.Where(channel => channel.Id == channelId && !channel.IsDeleted))
        .SingleOrDefaultAsync(cancellationToken);

    private Task<List<Channel>> LockChannelsAsync(
        IReadOnlyList<Guid> channelIds,
        CancellationToken cancellationToken)
    {
        var ids = channelIds.Order().ToArray();

        return (dbContext.Database.IsRelational()
            ? dbContext.Channels.FromSqlInterpolated(
                $"SELECT * FROM channels WHERE id = ANY({ids}) AND is_deleted = FALSE ORDER BY id FOR UPDATE")
            : dbContext.Channels.Where(channel => ids.Contains(channel.Id)).OrderBy(channel => channel.Id))
            .ToListAsync(cancellationToken);
    }

    private async Task RollbackAndClearAsync(IDbContextTransaction transaction)
    {
        await transaction.RollbackAsync(CancellationToken.None);
        dbContext.ChangeTracker.Clear();
    }

    /// <summary>
    /// Review ledgers exist only for approved and rejected channels, so any other status is rejected
    /// before it reaches the database.
    /// </summary>
    private IQueryable<Channel> ReviewChannels(ChannelStatus status, string? searchText)
    {
        if (status is not (ChannelStatus.APPROVED or ChannelStatus.REJECTED))
        {
            throw new ArgumentOutOfRangeException(
                nameof(status),
                status,
                "Only the approved and rejected channels are review ledgers.");
        }

        return ChannelsByStatus(status.ToString(), searchText);
    }

    private IQueryable<Channel> ChannelsByStatus(string statusValue, string? searchText)
    {
        var channels = dbContext.Channels
            .AsNoTracking()
            .Where(channel => channel.StatusValue == statusValue && !channel.IsDeleted);

        if (string.IsNullOrEmpty(searchText))
        {
            return channels;
        }

        // Portable LINQ rather than EF.Functions.ILike so the EF InMemory tests stay usable.
        // ToLower() is used instead of ToLowerInvariant() because the Npgsql provider cannot
        // translate the invariant form; ChannelSearchQueryTranslationTests guards this.
        var search = searchText.ToLower();

        return channels.Where(channel =>
            channel.Name.ToLower().Contains(search) ||
            channel.YoutubeChannelId.ToLower().Contains(search));
    }
}
