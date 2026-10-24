using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using YtFetcher.Core.Auditing;

namespace YtFetcher.Core.Channels;

/// <summary>
/// Persisted channel candidate and its review lifecycle.
/// </summary>
[Table("channels")]
public sealed class Channel : AuditableEntity
{
    [Key]
    [Column("id", TypeName = "uuid")]
    public Guid Id { get; set; }

    [Required]
    [MaxLength(64)]
    [Column("youtube_channel_id")]
    public string YoutubeChannelId { get; set; } = string.Empty;

    [Required]
    [MaxLength(500)]
    [Column("name")]
    public string Name { get; set; } = string.Empty;

    [Column("thumbnail_url", TypeName = "text")]
    public string? ThumbnailUrl { get; set; }

    [Required]
    [MaxLength(20)]
    [Column("status")]
    public string StatusValue { get; private set; } = nameof(ChannelStatus.PENDING);

    [NotMapped]
    public ChannelStatus Status
    {
        get => Enum.Parse<ChannelStatus>(StatusValue, ignoreCase: false);
        set => StatusValue = value.ToString();
    }

    [MaxLength(64)]
    [Column("uploads_playlist_id")]
    public string? UploadsPlaylistId { get; set; }

    [Required]
    [MaxLength(20)]
    [Column("backfill_status")]
    public string BackfillStatusValue { get; private set; } = nameof(BackfillStatus.PENDING);

    [NotMapped]
    public BackfillStatus BackfillStatus
    {
        get => Enum.Parse<BackfillStatus>(BackfillStatusValue, ignoreCase: false);
        set => BackfillStatusValue = value.ToString();
    }

    [Column("discovered_at", TypeName = "timestamp with time zone")]
    public DateTimeOffset DiscoveredAt { get; set; }

    [Column("approved_at", TypeName = "timestamp with time zone")]
    public DateTimeOffset? ApprovedAt { get; set; }

    [Column("approved_by_user_id", TypeName = "uuid")]
    public Guid? ApprovedByUserId { get; set; }

    [Column("rejected_at", TypeName = "timestamp with time zone")]
    public DateTimeOffset? RejectedAt { get; set; }

    [Column("rejected_by_user_id", TypeName = "uuid")]
    public Guid? RejectedByUserId { get; set; }

    [MaxLength(RejectionReasonMaxLength)]
    [Column("rejection_reason")]
    public string? RejectionReason { get; set; }

    [Column("last_upload_check_at", TypeName = "timestamp with time zone")]
    public DateTimeOffset? LastUploadCheckAt { get; set; }

    /// <summary>
    /// Pacific quota date whose paired first-page slot (one Query and one Video Batch Stats unit) this
    /// channel consumed at approval time. It is set only by a successful first-page slot claim and lets the
    /// page-one worker prove the slot was already reserved, so it never reserves either bucket again. It is
    /// not an audit field and is never set for a rejected or pending channel.
    /// </summary>
    [Column("first_page_quota_date", TypeName = "date")]
    public DateOnly? FirstPageQuotaDate { get; set; }

    [Column("backfill_cutoff_at", TypeName = "timestamp with time zone")]
    public DateTimeOffset? BackfillCutoffAt { get; set; }

    [Column("backfill_page_token", TypeName = "text")]
    public string? BackfillPageToken { get; set; }

    /// <summary>
    /// Durable oldest-waiting timestamp. The dispatch pump orders candidates by this value alone, so a
    /// channel returns to the back of the queue after every committed page and older waiters are
    /// naturally dispatched first.
    /// </summary>
    [Column("backfill_ready_at", TypeName = "timestamp with time zone")]
    public DateTimeOffset? BackfillReadyAt { get; set; }

    /// <summary>
    /// Count of successfully committed pages. It advances exactly once per committed page and is never
    /// reset by a retry, so a failed or deferred attempt resumes the same durable page.
    /// </summary>
    [Column("backfill_pages_completed")]
    public int BackfillPagesCompleted { get; set; }

    [Column("backfill_claim_token", TypeName = "uuid")]
    public Guid? BackfillClaimToken { get; set; }

    [Column("backfill_claimed_at", TypeName = "timestamp with time zone")]
    public DateTimeOffset? BackfillClaimedAt { get; set; }

    [Column("backfill_failure_count")]
    public int BackfillFailureCount { get; set; }

    [MaxLength(BackfillFailureReasonMaxLength)]
    [Column("backfill_failure_reason")]
    public string? BackfillFailureReason { get; set; }

    [Column("backfill_last_failed_at", TypeName = "timestamp with time zone")]
    public DateTimeOffset? BackfillLastFailedAt { get; set; }

    /// <summary>
    /// When the queue-enqueue attempt for this channel last started. Used as the scheduling lease:
    /// a recovery pass may take over a <see cref="BackfillStatus.SCHEDULING"/> row only after
    /// <see cref="BackfillClaimLifetime"/>. It describes queue scheduling only and never carries a
    /// YouTube/provider/persistence backfill failure.
    /// </summary>
    [Column("backfill_scheduling_attempted_at", TypeName = "timestamp with time zone")]
    public DateTimeOffset? BackfillSchedulingAttemptedAt { get; set; }

    /// <summary>
    /// Safe operator-facing reason a queue-enqueue attempt failed. Set only together with
    /// <see cref="BackfillStatus.SCHEDULING_FAILED"/> and cleared by a later scheduling transition.
    /// </summary>
    [MaxLength(BackfillSchedulingFailureReasonMaxLength)]
    [Column("backfill_scheduling_failure_reason")]
    public string? BackfillSchedulingFailureReason { get; set; }

    /// <summary>How long a page claim stays valid before a later job may reclaim it.</summary>
    public static readonly TimeSpan BackfillClaimLifetime = TimeSpan.FromMinutes(15);

    /// <summary>Video age horizon one backfill covers, in days.</summary>
    public const int BackfillHorizonDays = 730;

    /// <summary>Consecutive failures after which automatic work stops selecting the channel.</summary>
    public const int BackfillFailureLimit = 3;

    /// <summary>Maximum length of a persisted backfill failure reason, matching the database column.</summary>
    public const int BackfillFailureReasonMaxLength = 500;

    /// <summary>Maximum length of a persisted scheduling failure reason, matching the database column.</summary>
    public const int BackfillSchedulingFailureReasonMaxLength = 500;

    /// <summary>
    /// True when this channel can be claimed by the first-page backfill job: it is approved, not
    /// deleted, still waiting for its first page (<see cref="BackfillStatus.PENDING"/>), and has not
    /// reached the consecutive execution-failure limit. Scheduling states
    /// (<see cref="BackfillStatus.SCHEDULING"/>, <see cref="BackfillStatus.SCHEDULING_FAILED"/>) are
    /// deliberately excluded: a job must never run while its enqueue state is still ambiguous.
    /// </summary>
    public bool IsBackfillEligible =>
        Status == ChannelStatus.APPROVED &&
        !IsDeleted &&
        BackfillStatus == BackfillStatus.PENDING &&
        BackfillFailureCount < BackfillFailureLimit;

    /// <summary>
    /// True when the persisted cursor matches the durable page-progress shape: page one has zero
    /// committed pages and no cursor, and a continuation has at least one committed page and a cursor.
    /// The dispatch pump and the page worker both refuse a row that violates this shape.
    /// </summary>
    public bool HasEligibleBackfillCursor =>
        BackfillPagesCompleted == 0
            ? BackfillPageToken is null
            : BackfillPageToken is not null;

    /// <summary>True when a claim is persisted and still younger than <see cref="BackfillClaimLifetime"/>.</summary>
    public bool HasLiveBackfillClaim(DateTimeOffset utcNow) =>
        BackfillClaimToken is not null &&
        BackfillClaimedAt is not null &&
        BackfillClaimedAt.Value + BackfillClaimLifetime > utcNow;

    /// <summary>
    /// True when the dispatch pump may select this channel: approved, not deleted, waiting in
    /// <see cref="BackfillStatus.PENDING"/> without a claim, inside the optional positive page limit,
    /// with a valid cursor shape, and with a durable ready time that has arrived. The pump additionally
    /// requires an available quota allocation, which is checked inside the same transaction that
    /// reserves it, not on this entity.
    /// </summary>
    public bool IsPumpCandidate(int pageLimit, DateTimeOffset utcNow) =>
        IsBackfillEligible &&
        BackfillClaimToken is null &&
        (pageLimit <= 0 || BackfillPagesCompleted < pageLimit) &&
        HasEligibleBackfillCursor &&
        BackfillReadyAt is not null &&
        BackfillReadyAt.Value <= utcNow;

    /// <summary>
    /// Applies the pump's <c>PENDING -&gt; SCHEDULING</c> transition with a fresh scheduling lease and a
    /// cleared scheduling reason. The caller performs this inside the same short transaction that
    /// reserved the page's quota allocation. Returns <c>false</c> without mutating state when the
    /// channel is not waiting in <see cref="BackfillStatus.PENDING"/>.
    /// </summary>
    public bool TryBeginPumpScheduling(DateTimeOffset utcNow)
    {
        if (BackfillStatus != BackfillStatus.PENDING)
        {
            return false;
        }

        BackfillStatus = BackfillStatus.SCHEDULING;
        BackfillSchedulingAttemptedAt = utcNow;
        BackfillSchedulingFailureReason = null;
        ModifiedAt = utcNow;
        return true;
    }

    /// <summary>
    /// Applies the page worker's claim transition to <see cref="BackfillStatus.RUNNING"/>. A dispatched
    /// page is claimed from <see cref="BackfillStatus.SCHEDULING"/>; the automatic Hangfire retry of a
    /// failed page re-claims its durable cursor from <see cref="BackfillStatus.PENDING"/>. Returns
    /// <c>false</c> without mutating state when the channel is not an approved, non-deleted channel in
    /// one of those states, when the cursor shape is invalid, or when a live claim is still held.
    /// </summary>
    public bool TryClaimPage(Guid claimToken, DateTimeOffset utcNow)
    {
        if (Status != ChannelStatus.APPROVED || IsDeleted)
        {
            return false;
        }

        if (BackfillStatus is not (BackfillStatus.SCHEDULING or BackfillStatus.PENDING))
        {
            return false;
        }

        if (!HasEligibleBackfillCursor || HasLiveBackfillClaim(utcNow))
        {
            return false;
        }

        BackfillClaimToken = claimToken;
        BackfillClaimedAt = utcNow;
        BackfillStatus = BackfillStatus.RUNNING;
        ModifiedAt = utcNow;
        return true;
    }

    /// <summary>
    /// Records one committed page for its claim owner: the committed-page count advances exactly once,
    /// the failure state clears, and the claim is released. A completed channel clears its cursor;
    /// otherwise the durable next cursor is stored and the channel returns to
    /// <see cref="BackfillStatus.PENDING"/> with a new ready time, so older waiters keep going first.
    /// Returns <c>false</c> without mutating state when the claim is not owned by
    /// <paramref name="claimToken"/>.
    /// </summary>
    public bool TryRecordBackfillPageSuccess(
        Guid claimToken,
        bool isChannelComplete,
        string? nextPageToken,
        DateTimeOffset utcNow)
    {
        if (BackfillClaimToken != claimToken)
        {
            return false;
        }

        // A page that is not the last one must carry the continuation cursor, otherwise the durable
        // page-progress shape would be violated and the next pump pass could not resume this channel.
        if (!isChannelComplete)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(nextPageToken);
        }

        BackfillPagesCompleted += 1;
        ResetBackfillFailure(utcNow);
        BackfillClaimToken = null;
        BackfillClaimedAt = null;

        if (isChannelComplete)
        {
            BackfillStatus = BackfillStatus.COMPLETED;
            BackfillPageToken = null;
        }
        else
        {
            BackfillStatus = BackfillStatus.PENDING;
            BackfillPageToken = nextPageToken;
            BackfillReadyAt = utcNow;
        }

        ModifiedAt = utcNow;
        return true;
    }

    /// <summary>
    /// Releases the claim of a page that could not obtain quota. The channel returns to
    /// <see cref="BackfillStatus.PENDING"/> with its cursor, committed count, cutoff, ready time and
    /// reservation preserved, so a later pump pass resumes exactly this page. Returns <c>false</c>
    /// without mutating state when the claim is not owned by <paramref name="claimToken"/>.
    /// </summary>
    public bool TryDeferBackfillForQuota(Guid claimToken, DateTimeOffset utcNow)
    {
        if (!TryReleaseRunningClaim(claimToken, utcNow))
        {
            return false;
        }

        // Deferred work must be an ordinary candidate the next time the gate opens, so it carries a ready
        // time exactly like every other PENDING row. Without this the row would be PENDING with a null ready
        // time and the pump would never select it.
        BackfillReadyAt = utcNow;
        return true;
    }

    /// <summary>
    /// Abandons a claim whose lifetime expired: the claim is released, the channel returns to
    /// <see cref="BackfillStatus.PENDING"/> on the ordinary retry path with a new ready time, and the
    /// cursor, committed count, cutoff, reservation and failure state stay untouched so the retry
    /// resumes the same page. The owner that lost the claim must make no further provider call or
    /// state change. Returns <c>false</c> when the channel is not running a claim that already expired.
    /// </summary>
    public bool TryAbandonExpiredClaim(DateTimeOffset utcNow)
    {
        if (BackfillStatus != BackfillStatus.RUNNING || HasLiveBackfillClaim(utcNow))
        {
            return false;
        }

        BackfillClaimToken = null;
        BackfillClaimedAt = null;
        BackfillStatus = BackfillStatus.PENDING;
        BackfillReadyAt = utcNow;
        ModifiedAt = utcNow;
        return true;
    }

    /// <summary>
    /// Applies the dispatch pump's recovery transition for queue scheduling: a stale
    /// <see cref="BackfillStatus.SCHEDULING"/> lease is refreshed, and a
    /// <see cref="BackfillStatus.SCHEDULING_FAILED"/> row returns to
    /// <see cref="BackfillStatus.SCHEDULING"/> with its safe reason cleared, so the enqueue is retried
    /// without operator action. Returns <c>false</c> without mutating state for any other combination.
    /// </summary>
    public bool TryBeginRecoveredScheduling(DateTimeOffset utcNow)
    {
        if (Status != ChannelStatus.APPROVED || IsDeleted)
        {
            return false;
        }

        if (BackfillStatus is not (BackfillStatus.SCHEDULING or BackfillStatus.SCHEDULING_FAILED))
        {
            return false;
        }

        BackfillStatus = BackfillStatus.SCHEDULING;
        BackfillSchedulingFailureReason = null;
        BackfillSchedulingAttemptedAt = utcNow;
        ModifiedAt = utcNow;
        return true;
    }

    /// <summary>
    /// Applies the operator retry transition <c>FAILED -&gt; PENDING</c>: only the execution-failure
    /// metadata is cleared and the channel returns to the ordinary pump path with a new ready time. The
    /// claim, cursor, committed count, cutoff and quota reservation are deliberately left untouched, and
    /// nothing is enqueued or reserved here. Returns <c>false</c> without mutating state unless the
    /// channel is an approved, non-deleted channel whose page work is terminally failed.
    /// </summary>
    public bool TryResetFailedBackfill(DateTimeOffset utcNow, Guid actorUserId)
    {
        if (Status != ChannelStatus.APPROVED || IsDeleted || BackfillStatus != BackfillStatus.FAILED)
        {
            return false;
        }

        BackfillFailureCount = 0;
        BackfillFailureReason = null;
        BackfillLastFailedAt = null;
        BackfillStatus = BackfillStatus.PENDING;
        BackfillReadyAt = utcNow;
        ModifiedAt = utcNow;
        ModifiedByUserId = actorUserId;
        return true;
    }

    private bool TryReleaseRunningClaim(Guid claimToken, DateTimeOffset utcNow)
    {
        if (BackfillStatus != BackfillStatus.RUNNING || BackfillClaimToken != claimToken)
        {
            return false;
        }

        BackfillClaimToken = null;
        BackfillClaimedAt = null;
        BackfillStatus = BackfillStatus.PENDING;
        ModifiedAt = utcNow;
        return true;
    }

    /// <summary>
    /// Freezes the backfill horizon on the first successful page. Later pages keep the frozen value so
    /// a multi-day backfill cannot slowly lose older in-scope videos.
    /// </summary>
    public void FreezeBackfillCutoff(DateTimeOffset runStartedAt) =>
        BackfillCutoffAt ??= runStartedAt.AddDays(-BackfillHorizonDays);

    /// <summary>
    /// Records a successful M6 new-upload check. The caller passes the instant captured at the start of the
    /// monitoring phase, not a later clock reading, so an upload arriving while the phase is still running
    /// remains eligible for the next Pacific daily check. Only a fully persisted playlist page advances this
    /// marker; a provider or persistence failure leaves it unchanged.
    /// </summary>
    public void RecordUploadCheckSuccess(DateTimeOffset checkedAtUtc)
    {
        LastUploadCheckAt = checkedAtUtc;
        ModifiedAt = checkedAtUtc;
    }

    /// <summary>
    /// Records one consecutive execution failure for the job that owns <paramref name="claimToken"/>:
    /// the count, the safe reason and the timestamp are persisted and the owned claim is released in
    /// the same mutation. Below the failure limit the status stays/becomes
    /// <see cref="BackfillStatus.PENDING"/> so the same Hangfire job can retry it; only the failure that
    /// reaches <see cref="BackfillFailureLimit"/> sets <see cref="BackfillStatus.FAILED"/>. Returns
    /// <c>false</c> without mutating state when the claim is not owned by that token.
    /// </summary>
    public bool RecordBackfillFailure(Guid claimToken, string failureReason, DateTimeOffset utcNow)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(failureReason);

        var trimmedReason = failureReason.Trim();
        if (trimmedReason.Length > BackfillFailureReasonMaxLength)
        {
            throw new ArgumentException(
                $"A backfill failure reason cannot exceed {BackfillFailureReasonMaxLength} characters.",
                nameof(failureReason));
        }

        if (BackfillClaimToken != claimToken)
        {
            return false;
        }

        // The column is bounded by a check constraint, so the count never exceeds the limit.
        BackfillFailureCount = Math.Min(BackfillFailureCount + 1, BackfillFailureLimit);
        BackfillFailureReason = trimmedReason;
        BackfillLastFailedAt = utcNow;
        if (BackfillFailureCount >= BackfillFailureLimit)
        {
            BackfillStatus = BackfillStatus.FAILED;
        }
        else
        {
            // A retryable failure returns the row to the pump queue as well, so it must be dispatchable.
            BackfillStatus = BackfillStatus.PENDING;
            BackfillReadyAt = utcNow;
        }

        BackfillClaimToken = null;
        BackfillClaimedAt = null;
        ModifiedAt = utcNow;
        return true;
    }

    /// <summary>
    /// Clears the persisted failure state after a page persisted successfully. The caller still owns
    /// the claim and is responsible for the cursor/status transition.
    /// </summary>
    public void ResetBackfillFailure(DateTimeOffset utcNow)
    {
        BackfillFailureCount = 0;
        BackfillFailureReason = null;
        BackfillLastFailedAt = null;
        ModifiedAt = utcNow;
    }

    /// <summary>Maximum length of a persisted rejection reason, matching the database column.</summary>
    public const int RejectionReasonMaxLength = 500;

    /// <summary>
    /// Applies the <c>PENDING -&gt; APPROVED</c> lifecycle transition. Returns <c>false</c> without
    /// mutating state when the channel is not pending.
    /// </summary>
    public bool Approve(DateTimeOffset utcNow, Guid approvedByUserId)
    {
        if (Status != ChannelStatus.PENDING)
        {
            return false;
        }

        Status = ChannelStatus.APPROVED;
        ApprovedAt = utcNow;
        ApprovedByUserId = approvedByUserId;
        BackfillStatus = BackfillStatus.PENDING;
        ModifiedByUserId = approvedByUserId;
        ModifiedAt = utcNow;
        return true;
    }

    /// <summary>
    /// Applies the approval transition and the scheduling-start transition in one domain mutation:
    /// <c>PENDING -&gt; APPROVED</c> plus <c>backfill_status = SCHEDULING</c>, the scheduling lease
    /// timestamp, and a cleared scheduling reason. Returns <c>false</c> without mutating state when
    /// the channel is not pending.
    /// </summary>
    public bool TryBeginSchedulingAfterApproval(DateTimeOffset utcNow, Guid approvedByUserId)
    {
        if (!Approve(utcNow, approvedByUserId))
        {
            return false;
        }

        ResetForNewBackfillGeneration();
        BackfillStatus = BackfillStatus.SCHEDULING;
        BackfillSchedulingAttemptedAt = utcNow;
        BackfillSchedulingFailureReason = null;
        return true;
    }

    /// <summary>
    /// Returns a rejected channel to review. A running page is deliberately not interrupted: the caller must wait
    /// for that page to finish before a new approval creates a fresh backfill generation. A queued scheduling
    /// attempt that starts while the channel remains rejected cannot claim the page because claims require APPROVED.
    /// </summary>
    public bool TryReopenForReview(DateTimeOffset utcNow, Guid actorUserId)
    {
        if (Status != ChannelStatus.REJECTED ||
            IsDeleted ||
            BackfillStatus == BackfillStatus.RUNNING)
        {
            return false;
        }

        Status = ChannelStatus.PENDING;
        RejectedAt = null;
        RejectedByUserId = null;
        RejectionReason = null;
        ModifiedAt = utcNow;
        ModifiedByUserId = actorUserId;
        return true;
    }

    private void ResetForNewBackfillGeneration()
    {
        BackfillStatus = BackfillStatus.PENDING;
        BackfillPageToken = null;
        BackfillReadyAt = null;
        BackfillPagesCompleted = 0;
        BackfillClaimToken = null;
        BackfillClaimedAt = null;
        BackfillCutoffAt = null;
        BackfillFailureCount = 0;
        BackfillFailureReason = null;
        BackfillLastFailedAt = null;
        BackfillSchedulingAttemptedAt = null;
        BackfillSchedulingFailureReason = null;
        FirstPageQuotaDate = null;
        LastUploadCheckAt = null;
    }

    /// <summary>
    /// Creates a brand-new channel that was added manually as an already-approved first-page
    /// backfill subject: <c>APPROVED</c> plus <c>backfill_status = SCHEDULING</c>.
    /// </summary>
    /// <remarks>
    /// The caller persists the returned entity and then schedules it. Required canonical YouTube
    /// metadata (channel id, name, uploads playlist) must be present so the created row satisfies the
    /// same invariants as the approval lifecycle.
    /// </remarks>
    public static Channel CreateApprovedForScheduling(
        string youtubeChannelId,
        string name,
        string? thumbnailUrl,
        string uploadsPlaylistId,
        DateTimeOffset utcNow,
        Guid actorUserId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(youtubeChannelId);
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        ArgumentException.ThrowIfNullOrWhiteSpace(uploadsPlaylistId);

        return new Channel
        {
            Id = Guid.NewGuid(),
            YoutubeChannelId = youtubeChannelId.Trim(),
            Name = name.Trim(),
            ThumbnailUrl = thumbnailUrl,
            UploadsPlaylistId = uploadsPlaylistId.Trim(),
            Status = ChannelStatus.APPROVED,
            BackfillStatus = BackfillStatus.SCHEDULING,
            DiscoveredAt = utcNow,
            ApprovedAt = utcNow,
            ApprovedByUserId = actorUserId,
            BackfillSchedulingAttemptedAt = utcNow,
            IsDeleted = false,
            CreatedAt = utcNow,
            ModifiedAt = utcNow,
            CreatedByUserId = actorUserId,
            ModifiedByUserId = actorUserId
        };
    }

    /// <summary>
    /// Applies the <c>SCHEDULING -&gt; SCHEDULING_FAILED</c> transition with the exact safe reason and
    /// a refreshed scheduling lease. Returns <c>false</c> without mutating state when the channel is
    /// not in the expected scheduling state.
    /// </summary>
    public bool TryFailScheduling(string safeReason, DateTimeOffset utcNow)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(safeReason);

        var trimmedReason = safeReason.Trim();
        if (trimmedReason.Length > BackfillSchedulingFailureReasonMaxLength)
        {
            throw new ArgumentException(
                $"A scheduling failure reason cannot exceed {BackfillSchedulingFailureReasonMaxLength} characters.",
                nameof(safeReason));
        }

        if (BackfillStatus != BackfillStatus.SCHEDULING)
        {
            return false;
        }

        BackfillStatus = BackfillStatus.SCHEDULING_FAILED;
        BackfillSchedulingFailureReason = trimmedReason;
        BackfillSchedulingAttemptedAt = utcNow;
        ModifiedAt = utcNow;
        return true;
    }

    /// <summary>
    /// Refreshes only the scheduling lease timestamp so a recovery pass owns the row for another
    /// <see cref="BackfillClaimLifetime"/> without changing its scheduling state.
    /// </summary>
    public void RefreshSchedulingLease(DateTimeOffset utcNow)
    {
        BackfillSchedulingAttemptedAt = utcNow;
        ModifiedAt = utcNow;
    }

    /// <summary>
    /// Applies the rejection lifecycle transition from either <see cref="ChannelStatus.PENDING"/> or
    /// <see cref="ChannelStatus.APPROVED"/> and stores the trimmed reason. Returns <c>false</c>
    /// without mutating state for any other status. A blank or overlong reason is rejected before
    /// any state change. Only lifecycle, rejection, and modified-audit fields change; backfill
    /// state is deliberately left untouched.
    /// </summary>
    public bool Reject(DateTimeOffset utcNow, Guid? rejectedByUserId, string reason)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(reason);

        var trimmedReason = reason.Trim();
        if (trimmedReason.Length > RejectionReasonMaxLength)
        {
            throw new ArgumentException(
                $"A rejection reason cannot exceed {RejectionReasonMaxLength} characters.",
                nameof(reason));
        }

        if (Status is not (ChannelStatus.PENDING or ChannelStatus.APPROVED))
        {
            return false;
        }

        Status = ChannelStatus.REJECTED;
        RejectedAt = utcNow;
        RejectedByUserId = rejectedByUserId;
        RejectionReason = trimmedReason;
        ModifiedByUserId = rejectedByUserId;
        ModifiedAt = utcNow;
        return true;
    }

    /// <summary>
    /// Applies the <c>active -&gt; soft deleted</c> lifecycle transition. Returns <c>false</c> without
    /// mutating state when the channel is already deleted.
    /// </summary>
    public bool SoftDelete(DateTimeOffset utcNow, Guid? deletedByUserId)
    {
        if (IsDeleted)
        {
            return false;
        }

        IsDeleted = true;
        DeletedAt = utcNow;
        DeletedByUserId = deletedByUserId;
        ModifiedByUserId = deletedByUserId;
        ModifiedAt = utcNow;
        return true;
    }
}
