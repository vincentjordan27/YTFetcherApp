using YtFetcher.Core.Channels;

namespace YtFetcher.Api.Tests.Core;

/// <summary>
/// Page-work lifecycle rules of the M5 generic one-page backfill: claim from a dispatched or retried
/// durable cursor, per-page progress, completion, quota deferral, stale-claim abandonment, and the
/// operator retry that resumes the same page.
/// </summary>
public class ChannelBackfillPageLifecycleTests
{
    private const string ContinuationCursor = "cursor-page-2";
    private const string UploadsFailureReason = "Unable to retrieve uploads from YouTube. Try again later.";

    private static readonly DateTimeOffset Now = new(2026, 10, 5, 4, 0, 0, TimeSpan.Zero);

    private static readonly DateOnly QuotaDate = new(2026, 10, 4);

    [Fact]
    public void A_dispatched_channel_is_claimed_from_scheduling_into_running()
    {
        var channel = ScheduledChannel();
        var claimToken = Guid.NewGuid();

        Assert.True(channel.TryClaimPage(claimToken, Now));

        Assert.Equal(BackfillStatus.RUNNING, channel.BackfillStatus);
        Assert.Equal(claimToken, channel.BackfillClaimToken);
        Assert.Equal(Now, channel.BackfillClaimedAt);
    }

    [Fact]
    public void A_retry_reclaims_the_same_durable_cursor_from_pending()
    {
        var channel = ContinuationChannel();
        channel.BackfillStatus = BackfillStatus.PENDING;
        var claimToken = Guid.NewGuid();

        Assert.True(channel.TryClaimPage(claimToken, Now));

        Assert.Equal(BackfillStatus.RUNNING, channel.BackfillStatus);
        Assert.Equal(ContinuationCursor, channel.BackfillPageToken);
        Assert.Equal(1, channel.BackfillPagesCompleted);
    }

    [Fact]
    public void A_live_claim_blocks_a_second_page_worker()
    {
        var channel = ScheduledChannel();
        Assert.True(channel.TryClaimPage(Guid.NewGuid(), Now));

        Assert.False(channel.TryClaimPage(Guid.NewGuid(), Now.AddMinutes(1)));
    }

    [Fact]
    public void A_rejected_channel_cannot_be_claimed_but_its_owned_claim_can_still_finish()
    {
        var channel = ScheduledChannel();
        var claimToken = Guid.NewGuid();
        Assert.True(channel.TryClaimPage(claimToken, Now));

        Assert.True(channel.Reject(Now.AddMinutes(1), null, "Operator stopped this channel."));

        Assert.False(channel.TryClaimPage(Guid.NewGuid(), Now.AddMinutes(2)));
        Assert.True(channel.TryRecordBackfillPageSuccess(claimToken, true, null, Now.AddMinutes(3)));
        Assert.Equal(BackfillStatus.COMPLETED, channel.BackfillStatus);
    }

    [Fact]
    public void A_page_of_a_rejected_channel_cannot_be_claimed_from_scheduling()
    {
        var channel = ScheduledChannel();
        channel.Status = ChannelStatus.REJECTED;

        Assert.False(channel.TryClaimPage(Guid.NewGuid(), Now));
        Assert.False(channel.IsPumpCandidate(pageLimit: 0, Now));
    }

    [Fact]
    public void A_committed_page_advances_the_count_once_and_returns_to_pending_with_a_new_ready_time()
    {
        var channel = ClaimedPageOne();
        var claimToken = channel.BackfillClaimToken!.Value;

        Assert.True(channel.TryRecordBackfillPageSuccess(claimToken, false, ContinuationCursor, Now));

        Assert.Equal(1, channel.BackfillPagesCompleted);
        Assert.Equal(BackfillStatus.PENDING, channel.BackfillStatus);
        Assert.Equal(ContinuationCursor, channel.BackfillPageToken);
        Assert.Equal(Now, channel.BackfillReadyAt);
        Assert.Null(channel.BackfillClaimToken);
        Assert.Null(channel.BackfillClaimedAt);
        Assert.Equal(Now, channel.ModifiedAt);
    }

    [Fact]
    public void Only_the_claim_owner_can_commit_the_page()
    {
        var channel = ClaimedPageOne();

        Assert.False(channel.TryRecordBackfillPageSuccess(Guid.NewGuid(), true, null, Now));
        Assert.Equal(0, channel.BackfillPagesCompleted);
        Assert.Equal(BackfillStatus.RUNNING, channel.BackfillStatus);
    }

    [Fact]
    public void The_final_page_completes_the_channel_and_clears_the_cursor()
    {
        var channel = ContinuationChannel();
        var claimToken = Guid.NewGuid();
        Assert.True(channel.TryClaimPage(claimToken, Now));

        Assert.True(channel.TryRecordBackfillPageSuccess(claimToken, true, null, Now));

        Assert.Equal(BackfillStatus.COMPLETED, channel.BackfillStatus);
        Assert.Null(channel.BackfillPageToken);
        Assert.Equal(2, channel.BackfillPagesCompleted);
    }

    [Fact]
    public void A_page_that_is_not_the_last_one_must_carry_a_continuation_cursor()
    {
        var channel = ClaimedPageOne();

        Assert.ThrowsAny<ArgumentException>(
            () => channel.TryRecordBackfillPageSuccess(channel.BackfillClaimToken!.Value, false, null, Now));

        Assert.Equal(0, channel.BackfillPagesCompleted);
        Assert.Equal(BackfillStatus.RUNNING, channel.BackfillStatus);
    }

    [Fact]
    public void Quota_deferral_releases_the_claim_preserves_progress_and_assigns_a_ready_time()
    {
        var channel = ContinuationChannel();
        var claimToken = Guid.NewGuid();
        Assert.True(channel.TryClaimPage(claimToken, Now));
        var readyAt = channel.BackfillReadyAt;
        channel.FreezeBackfillCutoff(Now);
        var cutoff = channel.BackfillCutoffAt;

        Assert.True(channel.TryDeferBackfillForQuota(claimToken, Now.AddMinutes(5)));

        Assert.Equal(BackfillStatus.PENDING, channel.BackfillStatus);
        Assert.Equal(ContinuationCursor, channel.BackfillPageToken);
        Assert.Equal(1, channel.BackfillPagesCompleted);
        Assert.Equal(cutoff, channel.BackfillCutoffAt);

        // A deferred row must remain a dispatchable pump candidate, so it takes the deferral instant as its
        // ready time instead of keeping the old one.
        Assert.Equal(Now.AddMinutes(5), channel.BackfillReadyAt);
        Assert.NotEqual(readyAt, channel.BackfillReadyAt);
        Assert.Null(channel.BackfillClaimToken);
        Assert.False(channel.TryDeferBackfillForQuota(claimToken, Now.AddMinutes(6)));
    }

    [Fact]
    public void An_expired_claim_is_abandoned_onto_the_ordinary_retry_path()
    {
        var channel = ContinuationChannel();
        Assert.True(channel.TryClaimPage(Guid.NewGuid(), Now));
        var readyAt = channel.BackfillReadyAt;

        Assert.True(channel.TryAbandonExpiredClaim(Now + Channel.BackfillClaimLifetime));

        Assert.Equal(BackfillStatus.PENDING, channel.BackfillStatus);
        Assert.Equal(Now + Channel.BackfillClaimLifetime, channel.BackfillReadyAt);
        Assert.NotEqual(readyAt, channel.BackfillReadyAt);
        Assert.Equal(ContinuationCursor, channel.BackfillPageToken);
        Assert.Equal(1, channel.BackfillPagesCompleted);
        Assert.Null(channel.BackfillClaimToken);
    }

    [Fact]
    public void A_live_claim_is_never_abandoned()
    {
        var channel = ScheduledChannel();
        Assert.True(channel.TryClaimPage(Guid.NewGuid(), Now));

        Assert.False(channel.TryAbandonExpiredClaim(Now.AddMinutes(14)));
        Assert.Equal(BackfillStatus.RUNNING, channel.BackfillStatus);
    }

    [Fact]
    public void Three_owned_failures_leave_the_channel_terminally_failed_with_its_page_state()
    {
        var channel = ContinuationChannel();
        channel.FreezeBackfillCutoff(Now);
        var cutoff = channel.BackfillCutoffAt;

        RecordFailures(channel, Channel.BackfillFailureLimit);

        Assert.Equal(BackfillStatus.FAILED, channel.BackfillStatus);
        Assert.Equal(Channel.BackfillFailureLimit, channel.BackfillFailureCount);
        Assert.Equal(UploadsFailureReason, channel.BackfillFailureReason);
        Assert.Equal(ContinuationCursor, channel.BackfillPageToken);
        Assert.Equal(1, channel.BackfillPagesCompleted);
        Assert.Equal(cutoff, channel.BackfillCutoffAt);
        Assert.Null(channel.BackfillClaimToken);
        Assert.False(channel.IsPumpCandidate(pageLimit: 0, Now));
    }

    [Fact]
    public void Operator_retry_resets_only_failure_metadata_and_keeps_the_page_state()
    {
        var channel = ContinuationChannel();
        channel.FreezeBackfillCutoff(Now);
        var cutoff = channel.BackfillCutoffAt;
        RecordFailures(channel, Channel.BackfillFailureLimit);

        Assert.True(channel.TryResetFailedBackfill(Now.AddHours(1), Guid.NewGuid()));

        Assert.Equal(BackfillStatus.PENDING, channel.BackfillStatus);
        Assert.Equal(0, channel.BackfillFailureCount);
        Assert.Null(channel.BackfillFailureReason);
        Assert.Null(channel.BackfillLastFailedAt);
        Assert.Equal(ContinuationCursor, channel.BackfillPageToken);
        Assert.Equal(1, channel.BackfillPagesCompleted);
        Assert.Equal(cutoff, channel.BackfillCutoffAt);
        Assert.Equal(Now.AddHours(1), channel.BackfillReadyAt);
        Assert.True(channel.IsPumpCandidate(pageLimit: 0, Now.AddHours(1)));
        Assert.False(channel.TryResetFailedBackfill(Now.AddHours(2), Guid.NewGuid()));
    }

    [Fact]
    public void Operator_retry_only_applies_to_a_terminally_failed_channel()
    {
        var running = ClaimedPageOne();
        Assert.False(running.TryResetFailedBackfill(Now, Guid.NewGuid()));

        var rejected = ScheduledChannel();
        rejected.Status = ChannelStatus.REJECTED;
        Assert.False(rejected.TryResetFailedBackfill(Now, Guid.NewGuid()));

        var deleted = ScheduledChannel();
        deleted.SoftDelete(Now, null);
        Assert.False(deleted.TryResetFailedBackfill(Now, Guid.NewGuid()));
    }

    [Fact]
    public void The_pump_selects_only_ready_pending_candidates_below_the_page_limit()
    {
        var channel = ContinuationChannel();
        channel.BackfillReadyAt = Now;

        Assert.True(channel.IsPumpCandidate(pageLimit: 0, Now));
        Assert.True(channel.IsPumpCandidate(pageLimit: 3, Now));
        Assert.False(channel.IsPumpCandidate(pageLimit: 1, Now));

        channel.BackfillReadyAt = Now.AddMinutes(1);
        Assert.False(channel.IsPumpCandidate(pageLimit: 0, Now));

        channel.BackfillReadyAt = Now;
        channel.BackfillPagesCompleted = 3;
        Assert.False(channel.IsPumpCandidate(pageLimit: 3, Now));
        Assert.True(channel.IsPumpCandidate(pageLimit: 0, Now));

        channel.BackfillPagesCompleted = 1;
        channel.BackfillStatus = BackfillStatus.SCHEDULING;
        Assert.False(channel.IsPumpCandidate(pageLimit: 0, Now));
    }

    [Fact]
    public void A_pump_candidate_needs_a_ready_time_and_no_live_claim()
    {
        var channel = ContinuationChannel();
        channel.BackfillReadyAt = null;

        Assert.False(channel.IsPumpCandidate(pageLimit: 0, Now));

        channel.BackfillReadyAt = Now;
        channel.BackfillClaimToken = Guid.NewGuid();
        channel.BackfillClaimedAt = Now;
        Assert.False(channel.IsPumpCandidate(pageLimit: 0, Now));
    }

    [Fact]
    public void Cursor_shape_must_match_the_committed_page_count()
    {
        var pageOneWithCursor = ScheduledChannel();
        pageOneWithCursor.BackfillPageToken = "unexpected-cursor";
        Assert.False(pageOneWithCursor.HasEligibleBackfillCursor);
        Assert.False(pageOneWithCursor.TryClaimPage(Guid.NewGuid(), Now));

        var continuationWithoutCursor = ScheduledChannel();
        continuationWithoutCursor.BackfillPagesCompleted = 1;
        Assert.False(continuationWithoutCursor.HasEligibleBackfillCursor);
        Assert.False(continuationWithoutCursor.TryClaimPage(Guid.NewGuid(), Now));
    }

    [Fact]
    public void The_pump_transition_takes_a_pending_channel_into_scheduling_once()
    {
        var channel = ContinuationChannel();

        Assert.True(channel.TryBeginPumpScheduling(Now));

        Assert.Equal(BackfillStatus.SCHEDULING, channel.BackfillStatus);
        Assert.Equal(Now, channel.BackfillSchedulingAttemptedAt);
        Assert.False(channel.TryBeginPumpScheduling(Now.AddMinutes(1)));

        Assert.True(channel.TryClaimPage(Guid.NewGuid(), Now.AddMinutes(1)));
        Assert.Equal(BackfillStatus.RUNNING, channel.BackfillStatus);
    }

    [Fact]
    public void A_stale_scheduling_row_or_scheduling_failure_is_recovered_into_scheduling()
    {
        var staleScheduling = ScheduledChannel();
        Assert.True(staleScheduling.TryBeginRecoveredScheduling(Now));
        Assert.Equal(Now, staleScheduling.BackfillSchedulingAttemptedAt);

        var schedulingFailed = ScheduledChannel();
        schedulingFailed.BackfillStatus = BackfillStatus.SCHEDULING_FAILED;
        schedulingFailed.BackfillSchedulingFailureReason = "Backfill could not be scheduled. Try again.";
        Assert.True(schedulingFailed.TryBeginRecoveredScheduling(Now));
        Assert.Equal(BackfillStatus.SCHEDULING, schedulingFailed.BackfillStatus);
        Assert.Null(schedulingFailed.BackfillSchedulingFailureReason);

        var rejected = ScheduledChannel();
        rejected.Status = ChannelStatus.REJECTED;
        Assert.False(rejected.TryBeginRecoveredScheduling(Now));

        var running = ContinuationChannel();
        Assert.True(running.TryClaimPage(Guid.NewGuid(), Now));
        Assert.False(running.TryBeginRecoveredScheduling(Now));
    }

    /// <summary>
    /// Reaches the failure limit the way the worker does: every failure is recorded by a claim owner, so
    /// the durable count can only advance through an owned claim.
    /// </summary>
    private static void RecordFailures(Channel channel, int failureCount)
    {
        for (var attempt = 0; attempt < failureCount; attempt++)
        {
            var claimToken = Guid.NewGuid();
            Assert.True(channel.TryClaimPage(claimToken, Now.AddSeconds(attempt)));
            Assert.True(channel.RecordBackfillFailure(claimToken, UploadsFailureReason, Now.AddSeconds(attempt)));
        }
    }

    private static Channel ClaimedPageOne()
    {
        var channel = ScheduledChannel();
        Assert.True(channel.TryClaimPage(Guid.NewGuid(), Now));
        return channel;
    }

    private static Channel ScheduledChannel() => new()
    {
        Id = Guid.NewGuid(),
        YoutubeChannelId = "UC-page-lifecycle",
        Name = "Page lifecycle channel",
        Status = ChannelStatus.APPROVED,
        ApprovedAt = Now.AddDays(-1),
        BackfillStatus = BackfillStatus.SCHEDULING,
        BackfillSchedulingAttemptedAt = Now.AddMinutes(-1),
        IsDeleted = false,
        DiscoveredAt = Now.AddDays(-2),
        CreatedAt = Now.AddDays(-2),
        ModifiedAt = Now.AddDays(-1)
    };

    private static Channel ContinuationChannel()
    {
        var channel = ScheduledChannel();
        channel.BackfillStatus = BackfillStatus.PENDING;
        channel.BackfillPagesCompleted = 1;
        channel.BackfillPageToken = ContinuationCursor;
        channel.BackfillReadyAt = Now.AddMinutes(-1);
        return channel;
    }
}
