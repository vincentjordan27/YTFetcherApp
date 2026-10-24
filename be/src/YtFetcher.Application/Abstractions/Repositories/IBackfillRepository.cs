using YtFetcher.Application.Backfill.DTO.Result;
using YtFetcher.Core.BackfillRuns;
using YtFetcher.Core.Channels;

namespace YtFetcher.Application.Abstractions.Repositories;

/// <summary>
/// Persistence boundary for the cross-table backfill transactions. Claim ownership is verified in the
/// same PostgreSQL transaction that performs each write, and every claimed attempt is recorded in the
/// durable run history so an attempt always has one open row that a later step closes exactly once.
/// </summary>
public interface IBackfillRepository
{
    /// <summary>
    /// Atomically loads and claims one channel page for <paramref name="claimToken"/> and opens its run
    /// history row in the same transaction. A dispatched page is claimed from <c>SCHEDULING</c>; the
    /// automatic retry of a failed page re-claims the same durable cursor from <c>PENDING</c>.
    /// </summary>
    /// <remarks>
    /// For a page-one attempt whose <c>first_page_quota_date</c> is null or older than
    /// <paramref name="quotaDate"/>, the same transaction first claims a fresh paired first-page reservation
    /// (one attempt slot, one Queries unit, one Video Batch Stats unit) before any claim or run row is written.
    /// A refusal under a known exhaustion marker or the daily cap returns <c>null</c> with no state change and
    /// no run row, so the channel simply retries on a later Pacific date. Returns <c>null</c> when the channel
    /// is missing, not claimable, or already holds a live claim.
    /// </remarks>
    Task<Channel?> TryClaimPageAsync(
        Guid channelId,
        Guid claimToken,
        DateOnly quotaDate,
        BackfillTriggerSource triggerSource,
        DateTimeOffset utcNow,
        CancellationToken cancellationToken);

    /// <summary>
    /// Releases the claim of a page that could not obtain quota or capacity: the channel returns to
    /// <c>PENDING</c> with its cursor, committed count, cutoff and ready time preserved, and the open run
    /// row is closed as <see cref="BackfillRunStatus.ABANDONED"/> because the attempt produced no
    /// persistence outcome. Nothing is closed when the claim is no longer owned by
    /// <paramref name="claimToken"/>.
    /// </summary>
    Task ReleaseClaimAsync(
        Guid channelId,
        Guid claimToken,
        DateTimeOffset utcNow,
        CancellationToken cancellationToken);

    /// <summary>
    /// Persists one processed page in a single transaction: new videos, one initial snapshot each, the
    /// channel's page progress, the run row closed as succeeded, and the claim release. A page is
    /// terminal when the caller reports it so, which covers no next token, a page that walked past the
    /// frozen cutoff, and a page that reached a positive page limit. Throws when the transaction cannot
    /// commit.
    /// </summary>
    Task<BackfillPagePersistResult> PersistPageAsync(
        BackfillPageResult page,
        CancellationToken cancellationToken);

    /// <summary>
    /// Closes a claimed page attempt because the Queries or Video Batch Stats bucket could not provide its
    /// required reservation for the current Pacific date. The channel returns to the ordinary pending queue
    /// with its cursor, committed count, cutoff, and ready time preserved, the failure count is untouched, and
    /// the open run row is closed as <see cref="BackfillRunStatus.QUOTA_LIMIT_REACHED"/> with the bucket.
    /// Returns <c>false</c> without writing anything when the claim is no longer owned by
    /// <paramref name="claimToken"/>.
    /// </summary>
    Task<bool> DeferForQuotaLimitAsync(
        Guid channelId,
        Guid claimToken,
        QuotaLimitBucket bucket,
        DateTimeOffset utcNow,
        CancellationToken cancellationToken);

    /// <summary>
    /// Records one owned safe failure in its own transaction, closes the matching run row as failed with
    /// the resulting failure count, and releases the claim. Returns <c>null</c> when the claim no longer
    /// belongs to <paramref name="claimToken"/>, so another job's state is never overwritten.
    /// </summary>
    Task<BackfillFailureResult?> RecordFailureAsync(
        Guid channelId,
        Guid claimToken,
        string failureReason,
        DateTimeOffset utcNow,
        CancellationToken cancellationToken);

    /// <summary>
    /// Recovers at most <paramref name="maximumCount"/> stale queue rows in one transaction. A stale
    /// <c>SCHEDULING</c> lease or a <c>SCHEDULING_FAILED</c> row returns to <c>SCHEDULING</c> and its channel
    /// id is returned for exactly one re-enqueue; a running attempt whose claim is older than the claim
    /// lifetime has its open run row closed as abandoned, its claim released, and its channel returned to
    /// the ordinary candidate path, which is why it is counted instead of returned. Rows are locked with
    /// <c>FOR UPDATE SKIP LOCKED</c>, so concurrent pump instances never recover the same row twice.
    /// </summary>
    Task<BackfillRecoveryResult> RecoverStaleWorkAsync(
        DateTimeOffset staleBeforeUtc,
        DateTimeOffset utcNow,
        int maximumCount,
        CancellationToken cancellationToken);

    /// <summary>
    /// Selects and transitions at most <paramref name="maximumCount"/> due candidates in one short transaction.
    /// Candidates are locked with <c>FOR UPDATE SKIP LOCKED</c>, ordered only by <c>backfill_ready_at</c> then
    /// <c>id</c>, and a channel whose committed page count already reached a positive
    /// <paramref name="pageLimit"/> is never selected.
    /// </summary>
    /// <remarks>
    /// The pump deliberately owns no quota state: it never reads the ledger, never reserves a unit, and never
    /// writes the channel's reservation marker. Reserving one unit is the page worker's sole responsibility,
    /// immediately before its statistics call, because only the worker knows whether the page produced usable
    /// video IDs. A dispatched job that meets an exhausted bucket defers and keeps its cursor for a later
    /// dispatch window, which is an accepted durable queue outcome.
    /// </remarks>
    Task<BackfillDispatchResult> DispatchCandidatesAsync(
        int pageLimit,
        DateTimeOffset utcNow,
        int maximumCount,
        CancellationToken cancellationToken);
}
