using YtFetcher.Core.Channels;

namespace YtFetcher.Api.Tests.Core;

/// <summary>
/// Approved-channel rejection: it moves an approved channel to rejected without touching backfill
/// state, preserves an already-owned page claim, and makes every later claim ineligible.
/// </summary>
public class ChannelApprovedRejectionTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 1, 12, 0, 0, TimeSpan.Zero);
    private static readonly Guid ActorUserId = Guid.NewGuid();

    [Fact]
    public void Approving_then_rejecting_changes_only_lifecycle_rejection_and_audit_fields()
    {
        var channel = ApprovedChannel();
        var claimToken = Guid.NewGuid();
        channel.BackfillStatus = BackfillStatus.RUNNING;
        channel.BackfillPageToken = "next-page-token";
        channel.BackfillClaimToken = claimToken;
        channel.BackfillClaimedAt = Now.AddMinutes(-1);
        channel.BackfillCutoffAt = Now.AddDays(-730);
        channel.BackfillFailureCount = 2;
        channel.BackfillFailureReason = "Unable to retrieve video statistics from YouTube. Try again later.";
        channel.BackfillLastFailedAt = Now.AddHours(-2);
        channel.BackfillSchedulingAttemptedAt = Now.AddHours(-3);
        channel.BackfillSchedulingFailureReason = "Backfill could not be scheduled. Try again.";
        channel.ModifiedAt = Now.AddHours(-4);

        var rejected = channel.Reject(Now, rejectedByUserId: ActorUserId, "  Not a fit  ");

        Assert.True(rejected);
        Assert.Equal(ChannelStatus.REJECTED, channel.Status);
        Assert.Equal(Now, channel.RejectedAt);
        Assert.Equal("Not a fit", channel.RejectionReason);
        Assert.Equal(ActorUserId, channel.RejectedByUserId);
        Assert.Equal(ActorUserId, channel.ModifiedByUserId);
        Assert.Equal(Now, channel.ModifiedAt);

        // Every backfill/progress field is intentionally preserved.
        Assert.Equal(BackfillStatus.RUNNING, channel.BackfillStatus);
        Assert.Equal("next-page-token", channel.BackfillPageToken);
        Assert.Equal(claimToken, channel.BackfillClaimToken);
        Assert.Equal(Now.AddMinutes(-1), channel.BackfillClaimedAt);
        Assert.Equal(Now.AddDays(-730), channel.BackfillCutoffAt);
        Assert.Equal(2, channel.BackfillFailureCount);
        Assert.Equal(
            "Unable to retrieve video statistics from YouTube. Try again later.",
            channel.BackfillFailureReason);
        Assert.Equal(Now.AddHours(-2), channel.BackfillLastFailedAt);
        Assert.Equal(Now.AddHours(-3), channel.BackfillSchedulingAttemptedAt);
        Assert.Equal("Backfill could not be scheduled. Try again.", channel.BackfillSchedulingFailureReason);
    }

    [Fact]
    public void Rejecting_a_channel_that_is_not_pending_or_approved_does_nothing()
    {
        var rejectedChannel = ApprovedChannel();
        rejectedChannel.Status = ChannelStatus.REJECTED;
        var rejectedAtBefore = rejectedChannel.RejectedAt;
        var modifiedAtBefore = rejectedChannel.ModifiedAt;

        Assert.False(rejectedChannel.Reject(Now, null, "Again"));

        Assert.Equal(ChannelStatus.REJECTED, rejectedChannel.Status);
        Assert.Equal(rejectedAtBefore, rejectedChannel.RejectedAt);
        Assert.Equal(modifiedAtBefore, rejectedChannel.ModifiedAt);
    }

    [Fact]
    public void Reopening_a_rejected_completed_channel_preserves_its_backfill_history_state_until_reapproval()
    {
        var channel = ApprovedChannel();
        channel.BackfillStatus = BackfillStatus.COMPLETED;
        channel.BackfillPagesCompleted = 3;
        channel.BackfillCutoffAt = Now.AddDays(-365);
        channel.FirstPageQuotaDate = DateOnly.FromDateTime(Now.UtcDateTime.Date);
        Assert.True(channel.Reject(Now.AddMinutes(-2), ActorUserId, "No longer a fit"));

        var reopened = channel.TryReopenForReview(Now, ActorUserId);

        Assert.True(reopened);
        Assert.Equal(ChannelStatus.PENDING, channel.Status);
        Assert.Null(channel.RejectedAt);
        Assert.Null(channel.RejectedByUserId);
        Assert.Null(channel.RejectionReason);
        Assert.Equal(ActorUserId, channel.ModifiedByUserId);
        Assert.Equal(BackfillStatus.COMPLETED, channel.BackfillStatus);
        Assert.Equal(3, channel.BackfillPagesCompleted);
        Assert.Equal(Now.AddDays(-365), channel.BackfillCutoffAt);
        Assert.NotNull(channel.FirstPageQuotaDate);
    }

    [Fact]
    public void Reopening_a_rejected_running_channel_is_refused_until_its_page_finishes()
    {
        var channel = ApprovedChannel();
        channel.BackfillStatus = BackfillStatus.RUNNING;
        channel.BackfillClaimToken = Guid.NewGuid();
        channel.BackfillClaimedAt = Now.AddMinutes(-1);

        Assert.True(channel.Reject(Now.AddMinutes(-2), ActorUserId, "No longer a fit"));

        Assert.False(channel.TryReopenForReview(Now, ActorUserId));
        Assert.Equal(ChannelStatus.REJECTED, channel.Status);
    }

    private static Channel ApprovedChannel() => new()
    {
        Id = Guid.NewGuid(),
        YoutubeChannelId = $"UC-{Guid.NewGuid():N}",
        Name = "Approved Channel",
        Status = ChannelStatus.APPROVED,
        ApprovedAt = Now.AddDays(-2),
        BackfillStatus = BackfillStatus.PENDING,
        IsDeleted = false,
        DiscoveredAt = Now.AddDays(-3),
        CreatedAt = Now.AddDays(-3),
        ModifiedAt = Now.AddDays(-2)
    };
}
