using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using YtFetcher.Core.Auditing;

namespace YtFetcher.Core.Quotas;

/// <summary>
/// Daily accounting ledger for YouTube's independent provider quota buckets, one row per Pacific calendar
/// date. Earlier dates are historical and are never reset or deleted.
/// </summary>
/// <remarks>
/// The row mirrors the provider buckets, never individual modules: <c>queries_units_used</c> covers every
/// <c>channels.list</c>/<c>playlistItems.list</c> attempt, <c>video_batch_stats_units_used</c> covers every
/// <c>videos.batchGetStats</c> attempt, and <c>search_query_units_used</c> covers <c>search.list</c>. The
/// first-page pair is a durable allocation, not provider usage: it protects one Query and one Video Batch
/// Stats unit for every configured first-page slot before the daily workload consumes either bucket.
/// </remarks>
[Table("youtube_daily_quota_usage")]
public sealed class YouTubeDailyQuotaUsage : AuditableEntity
{
    [Key]
    [Column("id", TypeName = "uuid")]
    public Guid Id { get; set; }

    [Column("quota_date", TypeName = "date")]
    public DateOnly QuotaDate { get; set; }

    /// <summary>Ordinary units for every <c>channels.list</c> or <c>playlistItems.list</c> attempt, plus the pair consumed at approval.</summary>
    [Column("queries_units_used")]
    public int QueriesUnitsUsed { get; set; }

    /// <summary>Units for every <c>videos.batchGetStats</c> attempt, plus the pair consumed at approval.</summary>
    [Column("video_batch_stats_units_used")]
    public int VideoBatchStatsUnitsUsed { get; set; }

    /// <summary>Units for every <c>search.list</c> attempt; Search has its own provider bucket.</summary>
    [Column("search_query_units_used")]
    public int SearchQueryUnitsUsed { get; set; }

    /// <summary>Snapshot of the configured maximum approved channels for this Pacific date.</summary>
    [Column("first_page_attempt_limit")]
    public int FirstPageAttemptLimit { get; set; }

    /// <summary>First-page slots claimed by approval today. A rejected approval never increments it.</summary>
    [Column("first_page_attempt_used")]
    public int FirstPageAttemptUsed { get; set; }

    /// <summary>Set once when YouTube refused a <c>search.list</c> request for quota reasons on this date.</summary>
    [Column("search_quota_exhausted_at", TypeName = "timestamp with time zone")]
    public DateTimeOffset? SearchQuotaExhaustedAt { get; private set; }

    /// <summary>Set once when the Queries bucket was reported exhausted on this date.</summary>
    [Column("queries_quota_exhausted_at", TypeName = "timestamp with time zone")]
    public DateTimeOffset? QueriesQuotaExhaustedAt { get; private set; }

    /// <summary>Set once when the Video Batch Stats bucket was reported exhausted on this date.</summary>
    [Column("video_batch_stats_quota_exhausted_at", TypeName = "timestamp with time zone")]
    public DateTimeOffset? VideoBatchStatsQuotaExhaustedAt { get; private set; }

    /// <summary>
    /// Durable dispatch gate for the continuation pump. The scheduled phase job opens it once this quota
    /// date's daily phases have completed and both required backfill buckets still have usable capacity.
    /// </summary>
    [Column("backfill_dispatch_opened_at", TypeName = "timestamp with time zone")]
    public DateTimeOffset? BackfillDispatchOpenedAt { get; private set; }

    /// <summary>
    /// How long a daily-process claim stays owned before a later trigger may recover it. It is two hours: long
    /// enough that a slow but healthy run is never stolen, short enough that a host crash cannot block the date
    /// for the rest of the day.
    /// </summary>
    public static readonly TimeSpan DailyProcessClaimLifetime = TimeSpan.FromHours(2);

    /// <summary>
    /// Durable owner of today's daily processing run. It stops a second run from mistaking "every due row is
    /// currently claimed" for "statistics finished" and then opening the backfill gate early.
    /// </summary>
    [Column("daily_processing_claim_token", TypeName = "uuid")]
    public Guid? DailyProcessingClaimToken { get; private set; }

    [Column("daily_processing_claimed_at", TypeName = "timestamp with time zone")]
    public DateTimeOffset? DailyProcessingClaimedAt { get; private set; }

    /// <summary>Set once when the whole daily run finished. Never moved afterwards.</summary>
    [Column("daily_processing_completed_at", TypeName = "timestamp with time zone")]
    public DateTimeOffset? DailyProcessingCompletedAt { get; private set; }

    /// <summary>True when this quota date's durable dispatch gate has been opened.</summary>
    [NotMapped]
    public bool IsBackfillDispatchOpen => BackfillDispatchOpenedAt is not null;

    [NotMapped]
    public bool IsDailyProcessingCompleted => DailyProcessingCompletedAt is not null;

    [NotMapped]
    public bool IsSearchQuotaExhausted => SearchQuotaExhaustedAt is not null;

    [NotMapped]
    public bool IsQueriesQuotaExhausted => QueriesQuotaExhaustedAt is not null;

    [NotMapped]
    public bool IsVideoBatchStatsQuotaExhausted => VideoBatchStatsQuotaExhaustedAt is not null;

    /// <summary>
    /// True once Phase 0 has committed this date's configured first-page attempt limit. Before that, no
    /// provider reservation is valid and approval must be refused rather than silently spending capacity.
    /// </summary>
    [NotMapped]
    public bool IsFirstPageWindowReady => FirstPageAttemptLimit > 0;

    /// <summary>Slots still protected in both provider buckets for channels that may be approved today.</summary>
    [NotMapped]
    public int UnclaimedFirstPageSlots => Math.Max(0, FirstPageAttemptLimit - FirstPageAttemptUsed);

    /// <summary>True while the current claim is still inside its two-hour ownership lifetime.</summary>
    public bool HasLiveDailyProcessingClaim(DateTimeOffset utcNow) =>
        DailyProcessingClaimToken is not null &&
        DailyProcessingClaimedAt is not null &&
        DailyProcessingClaimedAt > utcNow - DailyProcessClaimLifetime;

    /// <summary>
    /// Decides and performs the durable daily-process claim for this date. A completed date is never claimed
    /// again and a live claim is never stolen; only a stale claim may be recovered.
    /// </summary>
    public DailyProcessClaimDecision TryClaimDailyProcessing(Guid claimToken, DateTimeOffset utcNow)
    {
        if (IsDailyProcessingCompleted)
        {
            return DailyProcessClaimDecision.AlreadyCompleted;
        }

        if (HasLiveDailyProcessingClaim(utcNow))
        {
            return DailyProcessClaimDecision.AlreadyRunning;
        }

        DailyProcessingClaimToken = claimToken;
        DailyProcessingClaimedAt = utcNow;
        ModifiedAt = utcNow;

        return DailyProcessClaimDecision.Claimed;
    }

    /// <summary>
    /// Gives up a handled claim without completing the date, so a same-day manual trigger may retry. A crash
    /// does not call this: the claim then survives until its recovery boundary.
    /// </summary>
    public void ReleaseDailyProcessingClaim()
    {
        DailyProcessingClaimToken = null;
        DailyProcessingClaimedAt = null;
    }

    /// <summary>
    /// Marks the date finished and gives up the claim. Completion is written once: a repeated call cannot move
    /// the first completion instant.
    /// </summary>
    public void CompleteDailyProcessing(DateTimeOffset utcNow)
    {
        DailyProcessingCompletedAt ??= utcNow;
        ReleaseDailyProcessingClaim();
        ModifiedAt = utcNow;
    }

    /// <summary>
    /// Opens the durable dispatch gate once, after the current quota date's daily phases have completed and
    /// both required backfill buckets still have usable capacity. Idempotent: the first opening instant is
    /// preserved.
    /// </summary>
    public void OpenBackfillDispatchGate(DateTimeOffset utcNow)
    {
        BackfillDispatchOpenedAt ??= utcNow;
        ModifiedAt = utcNow;
    }

    /// <summary>Records that YouTube refused a <c>search.list</c> request for quota reasons, once.</summary>
    public void MarkSearchQuotaExhausted(DateTimeOffset utcNow)
    {
        SearchQuotaExhaustedAt ??= utcNow;
        ModifiedAt = utcNow;
    }

    /// <summary>Records that the Queries bucket became unavailable, once.</summary>
    public void MarkQueriesQuotaExhausted(DateTimeOffset utcNow)
    {
        QueriesQuotaExhaustedAt ??= utcNow;
        ModifiedAt = utcNow;
    }

    /// <summary>Records that the Video Batch Stats bucket became unavailable, once.</summary>
    public void MarkVideoBatchStatsQuotaExhausted(DateTimeOffset utcNow)
    {
        VideoBatchStatsQuotaExhaustedAt ??= utcNow;
        ModifiedAt = utcNow;
    }
}
