using YtFetcher.Application.Backfill.DTO.Result;
using YtFetcher.Application.Videos.DTO.Result;
using YtFetcher.Core.Videos;

namespace YtFetcher.Application.Abstractions.Repositories;

/// <summary>
/// Persistence boundary for the daily tracked-video statistics phase. It owns the durable claim lifecycle and
/// the one success transaction; it never talks to the provider and never returns Application HTTP DTOs.
/// </summary>
public interface IVideoStatisticsRepository
{
    /// <summary>
    /// Releases statistics claims older than <paramref name="claimedBefore"/> so a crashed attempt cannot hide
    /// a due video forever. Only stale claims are touched; a live claim stays owned by its attempt.
    /// </summary>
    Task<int> ReleaseExpiredClaimsAsync(
        DateTimeOffset claimedBefore,
        DateTimeOffset utcNow,
        CancellationToken cancellationToken);

    /// <summary>
    /// Claims up to <paramref name="batchSize"/> due videos for <paramref name="claimToken"/> in one short
    /// transaction using <c>FOR UPDATE SKIP LOCKED</c>. A video is due only when it has no successful
    /// statistics fetch in the current Pacific quota-date window. Rows are ordered by their prior fetch then
    /// <c>id</c>. The transaction is committed before any provider call, so a concurrent daily process skips
    /// these rows instead of duplicating a request or a snapshot.
    /// </summary>
    Task<IReadOnlyList<Video>> ClaimDueBatchAsync(
        DateTimeOffset quotaDateStartUtc,
        DateTimeOffset utcNow,
        Guid claimToken,
        int batchSize,
        CancellationToken cancellationToken);

    /// <summary>
    /// Applies one successful provider batch inside one short transaction. The daily ledger row is locked and
    /// checked first, because the daily token is the ownership authority for the whole phase; then the exact
    /// requested video rows are locked and re-checked. If either check fails, the whole batch is rolled back and
    /// ownership loss is reported: nothing is partially persisted and a newer claim is never cleared. Otherwise it
    /// updates counts, inserts one snapshot per returned item, marks omitted provider ids
    /// <see cref="VideoAvailabilityStatus.UNAVAILABLE"/>, and clears the owned claims.
    /// </summary>
    Task<VideoStatisticsPersistResult> PersistSuccessAsync(
        DateOnly quotaDate,
        Guid dailyProcessClaimToken,
        IReadOnlyList<Guid> claimedVideoIds,
        Guid claimToken,
        IReadOnlyList<VideoStatsItemResult> items,
        DateTimeOffset fetchedAt,
        CancellationToken cancellationToken);

    /// <summary>
    /// Releases this batch's claims, and only claims still owned by this token, without changing any statistics
    /// value or due time. <paramref name="utcNow"/> is written to <c>modified_at</c> so a release is visible as
    /// a modification.
    /// </summary>
    Task ReleaseClaimsAsync(
        IReadOnlyList<Guid> claimedVideoIds,
        Guid claimToken,
        DateTimeOffset utcNow,
        CancellationToken cancellationToken);
}
