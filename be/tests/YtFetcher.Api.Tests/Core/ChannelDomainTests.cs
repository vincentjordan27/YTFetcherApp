using YtFetcher.Core.Channels;

namespace YtFetcher.Api.Tests.Core;

public class ChannelDomainTests
{
    private static readonly DateTimeOffset TransitionTime = new(2026, 9, 22, 12, 0, 0, TimeSpan.Zero);
    private static readonly DateTimeOffset Earlier = TransitionTime.AddDays(-2);
    private static readonly Guid AuditUserId = Guid.Parse("11111111-1111-1111-1111-111111111111");

    [Fact]
    public void Approve_from_pending_sets_status_audit_backfill_and_updated_fields()
    {
        var channel = CreatePendingChannel();

        var transitioned = channel.Approve(TransitionTime, AuditUserId);

        Assert.True(transitioned);
        Assert.Equal(ChannelStatus.APPROVED, channel.Status);
        Assert.Equal(TransitionTime, channel.ApprovedAt);
        Assert.Equal(AuditUserId, channel.ApprovedByUserId);
        Assert.Equal(BackfillStatus.PENDING, channel.BackfillStatus);
        Assert.Equal(TransitionTime, channel.ModifiedAt);
        Assert.Null(channel.RejectedAt);
        Assert.False(channel.IsDeleted);
    }

    [Fact]
    public void Approve_from_approved_returns_false_without_mutating_state()
    {
        var channel = CreatePendingChannel();
        channel.Approve(Earlier, AuditUserId);
        var approvedAt = channel.ApprovedAt;
        var updatedAt = channel.ModifiedAt;
        var backfillStatus = channel.BackfillStatus;

        var transitioned = channel.Approve(TransitionTime, AuditUserId);

        Assert.False(transitioned);
        Assert.Equal(ChannelStatus.APPROVED, channel.Status);
        Assert.Equal(approvedAt, channel.ApprovedAt);
        Assert.Equal(AuditUserId, channel.ApprovedByUserId);
        Assert.Equal(updatedAt, channel.ModifiedAt);
        Assert.Equal(backfillStatus, channel.BackfillStatus);
    }

    [Fact]
    public void Approve_from_rejected_returns_false_without_mutating_state()
    {
        var channel = CreatePendingChannel();
        channel.Reject(Earlier, AuditUserId, "Duplicate of an approved channel");
        var rejectedAt = channel.RejectedAt;
        var updatedAt = channel.ModifiedAt;

        var transitioned = channel.Approve(TransitionTime, AuditUserId);

        Assert.False(transitioned);
        Assert.Equal(ChannelStatus.REJECTED, channel.Status);
        Assert.Null(channel.ApprovedAt);
        Assert.Equal(rejectedAt, channel.RejectedAt);
        Assert.Equal(updatedAt, channel.ModifiedAt);
    }

    [Fact]
    public void Reject_from_pending_sets_status_audit_reason_and_updated_fields()
    {
        var channel = CreatePendingChannel();

        var transitioned = channel.Reject(TransitionTime, AuditUserId, "  Irrelevant content  ");

        Assert.True(transitioned);
        Assert.Equal(ChannelStatus.REJECTED, channel.Status);
        Assert.Equal(TransitionTime, channel.RejectedAt);
        Assert.Equal(AuditUserId, channel.RejectedByUserId);
        Assert.Equal("Irrelevant content", channel.RejectionReason);
        Assert.Equal(TransitionTime, channel.ModifiedAt);
        Assert.Null(channel.ApprovedAt);
        Assert.False(channel.IsDeleted);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void Reject_with_a_blank_reason_throws_without_mutating_state(string reason)
    {
        var channel = CreatePendingChannel();

        Assert.Throws<ArgumentException>(() => channel.Reject(TransitionTime, AuditUserId, reason));
        Assert.Equal(ChannelStatus.PENDING, channel.Status);
        Assert.Null(channel.RejectedAt);
        Assert.Null(channel.RejectionReason);
        Assert.Equal(Earlier, channel.ModifiedAt);
    }

    [Fact]
    public void Reject_with_a_reason_longer_than_the_column_limit_throws_without_mutating_state()
    {
        var channel = CreatePendingChannel();
        var reason = new string('a', Channel.RejectionReasonMaxLength + 1);

        Assert.Throws<ArgumentException>(() => channel.Reject(TransitionTime, AuditUserId, reason));
        Assert.Equal(ChannelStatus.PENDING, channel.Status);
        Assert.Null(channel.RejectionReason);
    }

    [Fact]
    public void Reject_from_an_already_rejected_channel_returns_false_without_mutating_state()
    {
        var channel = CreatePendingChannel();
        channel.Approve(Earlier, AuditUserId);
        channel.Reject(Earlier.AddHours(1), AuditUserId, "First reason");
        var updatedAt = channel.ModifiedAt;
        var rejectedAt = channel.RejectedAt;

        var transitioned = channel.Reject(TransitionTime, AuditUserId, "Not relevant");

        Assert.False(transitioned);
        Assert.Equal(ChannelStatus.REJECTED, channel.Status);
        Assert.Equal(rejectedAt, channel.RejectedAt);
        Assert.Equal("First reason", channel.RejectionReason);
        Assert.Equal(updatedAt, channel.ModifiedAt);
    }

    [Fact]
    public void SoftDelete_from_active_sets_deleted_and_updated_fields()
    {
        var channel = CreatePendingChannel();

        var transitioned = channel.SoftDelete(TransitionTime, AuditUserId);

        Assert.True(transitioned);
        Assert.True(channel.IsDeleted);
        Assert.Equal(TransitionTime, channel.DeletedAt);
        Assert.Equal(AuditUserId, channel.DeletedByUserId);
        Assert.Equal(TransitionTime, channel.ModifiedAt);
    }

    [Fact]
    public void SoftDelete_when_already_deleted_returns_false_without_mutating_state()
    {
        var channel = CreatePendingChannel();
        channel.SoftDelete(Earlier, AuditUserId);
        var deletedAt = channel.DeletedAt;
        var updatedAt = channel.ModifiedAt;

        var transitioned = channel.SoftDelete(TransitionTime, null);

        Assert.False(transitioned);
        Assert.True(channel.IsDeleted);
        Assert.Equal(deletedAt, channel.DeletedAt);
        Assert.Equal(AuditUserId, channel.DeletedByUserId);
        Assert.Equal(updatedAt, channel.ModifiedAt);
    }

    private static Channel CreatePendingChannel() => new()
    {
        Id = Guid.NewGuid(),
        YoutubeChannelId = "UC-domain",
        Name = "Domain channel",
        ThumbnailUrl = "https://example.com/domain.jpg",
        Status = ChannelStatus.PENDING,
        UploadsPlaylistId = "PL-domain",
        BackfillStatus = BackfillStatus.PENDING,
        IsDeleted = false,
        DiscoveredAt = Earlier,
        CreatedAt = Earlier,
        ModifiedAt = Earlier
    };
}
