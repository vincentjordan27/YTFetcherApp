using YtFetcher.Core.Channels;

namespace YtFetcher.Api.Tests.Core;

/// <summary>
/// Scheduling state transitions for the M5 approved-channel backfill: approval starts scheduling in
/// one mutation, and the completion/failure/retry transitions only apply from their expected state.
/// </summary>
public class ChannelSchedulingDomainTests
{
    private static readonly DateTimeOffset Now = new(2026, 9, 30, 9, 0, 0, TimeSpan.Zero);

    private const string SafeSchedulingReason = "Backfill could not be scheduled. Try again.";

    [Fact]
    public void Approval_starts_scheduling_in_one_transition()
    {
        var channel = PendingChannel();

        Assert.True(channel.TryBeginSchedulingAfterApproval(Now, approvedByUserId: Guid.NewGuid()));

        Assert.Equal(ChannelStatus.APPROVED, channel.Status);
        Assert.Equal(BackfillStatus.SCHEDULING, channel.BackfillStatus);
        Assert.Equal(Now, channel.ApprovedAt);
        Assert.Equal(Now, channel.BackfillSchedulingAttemptedAt);
        Assert.Null(channel.BackfillSchedulingFailureReason);
    }

    [Fact]
    public void Reapproval_starts_a_new_backfill_generation_without_deleting_prior_video_data()
    {
        var channel = PendingChannel();
        channel.BackfillStatus = BackfillStatus.COMPLETED;
        channel.BackfillPagesCompleted = 4;
        channel.BackfillPageToken = null;
        channel.BackfillCutoffAt = Now.AddDays(-365);
        channel.FirstPageQuotaDate = DateOnly.FromDateTime(Now.UtcDateTime.Date);
        channel.LastUploadCheckAt = Now.AddDays(-1);
        channel.BackfillFailureCount = 2;
        channel.BackfillFailureReason = "Old failure";

        Assert.True(channel.TryBeginSchedulingAfterApproval(Now, approvedByUserId: Guid.NewGuid()));

        Assert.Equal(ChannelStatus.APPROVED, channel.Status);
        Assert.Equal(BackfillStatus.SCHEDULING, channel.BackfillStatus);
        Assert.Equal(0, channel.BackfillPagesCompleted);
        Assert.Null(channel.BackfillPageToken);
        Assert.Null(channel.BackfillCutoffAt);
        Assert.Null(channel.FirstPageQuotaDate);
        Assert.Null(channel.LastUploadCheckAt);
        Assert.Equal(0, channel.BackfillFailureCount);
        Assert.Null(channel.BackfillFailureReason);
    }

    [Fact]
    public void Approval_cannot_start_scheduling_when_the_channel_is_not_pending()
    {
        var channel = PendingChannel();
        channel.Status = ChannelStatus.APPROVED;

        Assert.False(channel.TryBeginSchedulingAfterApproval(Now, approvedByUserId: Guid.NewGuid()));
        Assert.NotEqual(BackfillStatus.SCHEDULING, channel.BackfillStatus);
    }

    [Fact]
    public void Failing_scheduling_stores_the_exact_reason_and_only_applies_while_scheduling()
    {
        var channel = SchedulingChannel();

        Assert.True(channel.TryFailScheduling(SafeSchedulingReason, Now));

        Assert.Equal(BackfillStatus.SCHEDULING_FAILED, channel.BackfillStatus);
        Assert.Equal(SafeSchedulingReason, channel.BackfillSchedulingFailureReason);
        Assert.Equal(Now, channel.BackfillSchedulingAttemptedAt);

        Assert.False(channel.TryFailScheduling(SafeSchedulingReason, Now));
    }

    [Fact]
    public void Refreshing_the_scheduling_lease_only_moves_the_timestamp()
    {
        var channel = SchedulingChannel();
        var statusBefore = channel.BackfillStatus;

        channel.RefreshSchedulingLease(Now.AddMinutes(15));

        Assert.Equal(statusBefore, channel.BackfillStatus);
        Assert.Equal(Now.AddMinutes(15), channel.BackfillSchedulingAttemptedAt);
        Assert.Equal(Now.AddMinutes(15), channel.ModifiedAt);
    }

    private static Channel PendingChannel() => new()
    {
        Id = Guid.NewGuid(),
        YoutubeChannelId = "UC-scheduling-domain",
        Name = "Scheduling domain channel",
        Status = ChannelStatus.PENDING,
        BackfillStatus = BackfillStatus.PENDING,
        IsDeleted = false,
        DiscoveredAt = Now.AddDays(-2),
        CreatedAt = Now.AddDays(-2),
        ModifiedAt = Now.AddDays(-1)
    };

    private static Channel SchedulingChannel()
    {
        var channel = PendingChannel();
        Assert.True(channel.TryBeginSchedulingAfterApproval(Now.AddMinutes(-1), approvedByUserId: Guid.NewGuid()));
        return channel;
    }
}
