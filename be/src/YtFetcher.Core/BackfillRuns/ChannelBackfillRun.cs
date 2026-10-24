using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using YtFetcher.Core.Auditing;

namespace YtFetcher.Core.BackfillRuns;

/// <summary>
/// Durable history of exactly one claimed backfill page attempt. One row is created when a worker
/// claims a channel page and is closed as <see cref="BackfillRunStatus.SUCCEEDED"/>,
/// <see cref="BackfillRunStatus.FAILED"/>, or <see cref="BackfillRunStatus.ABANDONED"/>.
/// </summary>
/// <remarks>
/// The row stores only operator-readable state: no Hangfire job identifier, provider payload,
/// credential, stack trace, or raw exception text may be persisted here. The row deliberately has no
/// navigation property to <c>Channel</c>; the list and detail reads join explicitly in Infrastructure.
/// </remarks>
[Table("channel_backfill_runs")]
public sealed class ChannelBackfillRun : AuditableEntity
{
    [Key]
    [Column("id", TypeName = "uuid")]
    public Guid Id { get; set; }

    [Column("channel_id", TypeName = "uuid")]
    public Guid ChannelId { get; set; }

    /// <summary>
    /// The channel-page claim this attempt owns. A stale-claim recovery pass matches this token, which
    /// is why the database constrains it to be unique across runs.
    /// </summary>
    [Column("claim_token", TypeName = "uuid")]
    public Guid ClaimToken { get; set; }

    [Required]
    [MaxLength(20)]
    [Column("trigger_source")]
    public string TriggerSourceValue { get; private set; } = nameof(BackfillTriggerSource.APPROVAL);

    [NotMapped]
    public BackfillTriggerSource TriggerSource
    {
        get => Enum.Parse<BackfillTriggerSource>(TriggerSourceValue, ignoreCase: false);
        set => TriggerSourceValue = value.ToString();
    }

    /// <summary>
    /// One-based page number of this attempt: the channel's completed-page count plus one when the
    /// attempt was claimed. A retry of a failed page keeps the same number because the durable cursor
    /// and completed count do not move on failure.
    /// </summary>
    [Column("page_number")]
    public int PageNumber { get; set; }

    [Required]
    [MaxLength(20)]
    [Column("status")]
    public string StatusValue { get; private set; } = nameof(BackfillRunStatus.RUNNING);

    [NotMapped]
    public BackfillRunStatus Status
    {
        get => Enum.Parse<BackfillRunStatus>(StatusValue, ignoreCase: false);
        set => StatusValue = value.ToString();
    }

    [Column("started_at", TypeName = "timestamp with time zone")]
    public DateTimeOffset StartedAt { get; set; }

    [Column("completed_at", TypeName = "timestamp with time zone")]
    public DateTimeOffset? CompletedAt { get; private set; }

    [MaxLength(FailureReasonMaxLength)]
    [Column("failure_reason")]
    public string? FailureReason { get; private set; }

    /// <summary>
    /// The channel's consecutive execution-failure count at the moment this attempt was closed as
    /// failed. It is only set together with <see cref="BackfillRunStatus.FAILED"/>.
    /// </summary>
    [Column("failure_count_at_time")]
    public int? FailureCountAtTime { get; private set; }

    /// <summary>
    /// Provider bucket that reached its limit when this attempt was closed as
    /// <see cref="BackfillRunStatus.QUOTA_LIMIT_REACHED"/>. It is null for every other status.
    /// </summary>
    [MaxLength(30)]
    [Column("quota_limit_bucket")]
    public string? QuotaLimitBucketValue { get; private set; }

    [NotMapped]
    public QuotaLimitBucket? QuotaLimitBucket
    {
        get => QuotaLimitBucketValue is null
            ? null
            : Enum.Parse<QuotaLimitBucket>(QuotaLimitBucketValue, ignoreCase: false);
        private set => QuotaLimitBucketValue = value?.ToString();
    }

    /// <summary>Maximum length of a persisted run failure reason, matching the database column.</summary>
    public const int FailureReasonMaxLength = 500;

    /// <summary>
    /// Opens the history row for a claimed page. The caller must already own the channel page claim so
    /// the unique claim token identifies exactly one open attempt.
    /// </summary>
    public static ChannelBackfillRun Start(
        Guid channelId,
        Guid claimToken,
        BackfillTriggerSource triggerSource,
        int pageNumber,
        DateTimeOffset startedAt)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(pageNumber, 1);

        return new ChannelBackfillRun
        {
            Id = Guid.NewGuid(),
            ChannelId = channelId,
            ClaimToken = claimToken,
            TriggerSource = triggerSource,
            PageNumber = pageNumber,
            Status = BackfillRunStatus.RUNNING,
            StartedAt = startedAt,
            IsDeleted = false,
            CreatedAt = startedAt,
            ModifiedAt = startedAt
        };
    }

    /// <summary>
    /// Closes a still-running attempt as succeeded. Returns <c>false</c> without mutating state when
    /// the row is already terminal, so a late writer cannot rewrite a closed attempt.
    /// </summary>
    public bool Succeed(DateTimeOffset utcNow) => TryClose(BackfillRunStatus.SUCCEEDED, utcNow);

    /// <summary>
    /// Closes a still-running attempt with the readable reason and the channel's failure count at the
    /// time of failure. Returns <c>false</c> without mutating state when the row is already terminal.
    /// </summary>
    public bool Fail(string failureReason, int failureCountAtTime, DateTimeOffset utcNow)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(failureReason);
        ArgumentOutOfRangeException.ThrowIfNegative(failureCountAtTime);

        var trimmedReason = failureReason.Trim();
        if (trimmedReason.Length > FailureReasonMaxLength)
        {
            throw new ArgumentException(
                $"A backfill run failure reason cannot exceed {FailureReasonMaxLength} characters.",
                nameof(failureReason));
        }

        if (Status != BackfillRunStatus.RUNNING)
        {
            return false;
        }

        // A failed attempt is the only state that carries failure detail, matching the database
        // check constraint that clears these columns for every other terminal state.
        FailureReason = trimmedReason;
        FailureCountAtTime = failureCountAtTime;
        return TryClose(BackfillRunStatus.FAILED, utcNow);
    }

    /// <summary>
    /// Closes a still-running attempt because a provider quota bucket was unavailable for the current Pacific
    /// date. It carries the bucket that reached its limit and no failure detail, matching the database check
    /// constraint. Returns <c>false</c> without mutating state when the row is already terminal.
    /// </summary>
    public bool ReachQuotaLimit(QuotaLimitBucket bucket, DateTimeOffset utcNow)
    {
        if (Status != BackfillRunStatus.RUNNING)
        {
            return false;
        }

        QuotaLimitBucket = bucket;
        return TryClose(BackfillRunStatus.QUOTA_LIMIT_REACHED, utcNow);
    }

    /// <summary>
    /// Closes a still-running attempt without a provider or persistence outcome because a recovery
    /// pass took over its stale claim. Returns <c>false</c> without mutating state when the row is
    /// already terminal.
    /// </summary>
    public bool Abandon(DateTimeOffset utcNow) => TryClose(BackfillRunStatus.ABANDONED, utcNow);

    private bool TryClose(BackfillRunStatus terminalStatus, DateTimeOffset utcNow)
    {
        if (Status != BackfillRunStatus.RUNNING)
        {
            return false;
        }

        Status = terminalStatus;
        CompletedAt = utcNow;
        ModifiedAt = utcNow;
        return true;
    }
}
