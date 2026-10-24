using YtFetcher.Application.Quotas.DTO.Result;
using YtFetcher.Core.Quotas;

namespace YtFetcher.Application.Abstractions.Repositories;

/// <summary>
/// Persistence boundary for the daily provider-bucket quota ledger, which is the durable owner of the daily
/// processing run. Every reservation is one atomic conditional update that either reserves exactly one unit
/// or refuses; the read is a plain, soft-delete-filtered row lookup. It never exposes EF entities or SQL to
/// Application.
/// </summary>
public interface IDailyQuotaRepository
{
    /// <summary>
    /// Reads the active ledger row for <paramref name="quotaDate"/>, or <c>null</c> when that Pacific
    /// calendar date has no ledger row. Soft-deleted rows are never returned.
    /// </summary>
    Task<YouTubeDailyQuotaUsage?> GetForDateAsync(DateOnly quotaDate, CancellationToken cancellationToken);

    /// <summary>
    /// Phase 0 of the daily run: creates the Pacific date's ledger row when missing and snapshots the
    /// configured first-page attempt cap into <c>first_page_attempt_limit</c>. Until this commits, no
    /// reservation is valid and approval/discovery/manual lookup must be refused rather than spend capacity.
    /// </summary>
    Task EnsureLedgerAsync(DateOnly quotaDate, CancellationToken cancellationToken);

    /// <summary>
    /// Atomically tries to become the one durable owner of the date's daily processing run. A completed date
    /// is never claimed, a live claim is never stolen, and only a claim older than
    /// <see cref="YouTubeDailyQuotaUsage.DailyProcessClaimLifetime"/> may be recovered.
    /// </summary>
    Task<DailyProcessClaimDecision> TryClaimDailyProcessAsync(
        DateOnly quotaDate,
        Guid claimToken,
        DateTimeOffset utcNow,
        CancellationToken cancellationToken);

    /// <summary>
    /// Gives up the matching daily-process claim without completing the date, so a same-day manual trigger can
    /// retry. A claim another attempt already recovered is left untouched.
    /// </summary>
    Task ReleaseDailyProcessClaimAsync(
        DateOnly quotaDate,
        Guid claimToken,
        CancellationToken cancellationToken);

    /// <summary>
    /// One-way completion for the matching daily owner: writes <c>daily_processing_completed_at</c> once,
    /// clears the claim, and opens the continuation gate in the same statement only when both required
    /// backfill buckets still have a positive remainder after the protected unclaimed first-page slots. The
    /// remainders are computed from the committed counters at that instant, so no stale allocation is stored.
    /// Returns <c>Finalized = false</c> when this caller does not own the date or the date was already
    /// completed.
    /// </summary>
    Task<DailyFinalizationResult> FinalizeDailyProcessingAsync(
        DateOnly quotaDate,
        Guid claimToken,
        DateTimeOffset utcNow,
        CancellationToken cancellationToken);

    /// <summary>
    /// Reserves one ordinary unit from the general Queries bucket, protecting every unclaimed first-page slot.
    /// When <paramref name="dailyProcessClaimToken"/> is supplied the reservation is additionally fenced to that
    /// daily owner; a non-matching or completed date returns <see cref="QuotaReservationResult.OwnershipLost"/>.
    /// Discovery, manual lookup, and M5 continuations pass <c>null</c>.
    /// </summary>
    Task<QuotaReservationResult> TryReserveQueriesUnitAsync(
        DateOnly quotaDate,
        Guid? dailyProcessClaimToken,
        CancellationToken cancellationToken);

    /// <summary>
    /// Reserves one ordinary unit from the independent Video Batch Stats bucket, protecting every unclaimed
    /// first-page slot. The optional daily-owner fence behaves exactly as in
    /// <see cref="TryReserveQueriesUnitAsync"/>; M7 and M6 pass their claim token, M5 continuations pass
    /// <c>null</c>.
    /// </summary>
    Task<QuotaReservationResult> TryReserveVideoBatchStatsUnitAsync(
        DateOnly quotaDate,
        Guid? dailyProcessClaimToken,
        CancellationToken cancellationToken);

    /// <summary>Reserves one unit from the independent Search bucket (discovery only; no daily-owner fence).</summary>
    Task<QuotaReservationResult> TryReserveSearchUnitAsync(
        DateOnly quotaDate,
        Guid? dailyProcessClaimToken,
        CancellationToken cancellationToken);

    /// <summary>Records once that YouTube refused a <c>search.list</c> request for quota reasons.</summary>
    Task MarkSearchQuotaExhaustedAsync(
        DateOnly quotaDate,
        DateTimeOffset utcNow,
        CancellationToken cancellationToken);

    /// <summary>Records once that the Queries bucket became unavailable for this date.</summary>
    Task MarkQueriesQuotaExhaustedAsync(
        DateOnly quotaDate,
        DateTimeOffset utcNow,
        CancellationToken cancellationToken);

    /// <summary>Records once that the Video Batch Stats bucket became unavailable for this date.</summary>
    Task MarkVideoBatchStatsQuotaExhaustedAsync(
        DateOnly quotaDate,
        DateTimeOffset utcNow,
        CancellationToken cancellationToken);
}
