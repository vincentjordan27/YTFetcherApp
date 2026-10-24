using YtFetcher.Core.Channels;

namespace YtFetcher.Api.Tests.Core;

/// <summary>
/// Persisted failure lifecycle for the M5 approved-channel backfill: consecutive counting, the
/// automatic-failure limit, quota deferral, claim release, and the successful-page reset.
/// </summary>
public class ChannelBackfillFailureTests
{
    private static readonly DateTimeOffset Now = new(2026, 9, 30, 4, 0, 0, TimeSpan.Zero);

    private const string Reason = "Unable to retrieve uploads from YouTube. Try again later.";

    [Fact]
    public void First_and_second_failures_stay_pending_and_eligible_and_release_the_claim()
    {
        var channel = ClaimedChannel(out var claimToken);

        Assert.True(channel.RecordBackfillFailure(claimToken, Reason, Now));

        Assert.Equal(1, channel.BackfillFailureCount);
        Assert.Equal(BackfillStatus.PENDING, channel.BackfillStatus);
        Assert.Equal(Reason, channel.BackfillFailureReason);
        Assert.Equal(Now, channel.BackfillLastFailedAt);
        Assert.Null(channel.BackfillClaimToken);
        Assert.True(channel.IsBackfillEligible);

        // The same first-page job can claim the still-PENDING channel again for its delayed retry.
        Assert.True(channel.TryClaimPage(claimToken, Now.AddMinutes(20)));
        Assert.True(channel.RecordBackfillFailure(claimToken, Reason, Now.AddMinutes(20)));

        Assert.Equal(2, channel.BackfillFailureCount);
        Assert.Equal(BackfillStatus.PENDING, channel.BackfillStatus);
        Assert.Equal(Now.AddMinutes(20), channel.BackfillLastFailedAt);
        Assert.True(channel.IsBackfillEligible);
    }

    [Fact]
    public void Third_consecutive_failure_marks_the_channel_failed_and_not_claimable()
    {
        var channel = ClaimedChannel(out var claimToken);
        Assert.True(channel.RecordBackfillFailure(claimToken, Reason, Now));
        Assert.True(channel.TryClaimPage(claimToken, Now.AddMinutes(20)));
        Assert.True(channel.RecordBackfillFailure(claimToken, Reason, Now.AddMinutes(20)));
        Assert.True(channel.TryClaimPage(claimToken, Now.AddMinutes(40)));
        Assert.True(channel.RecordBackfillFailure(claimToken, Reason, Now.AddMinutes(40)));

        Assert.Equal(Channel.BackfillFailureLimit, channel.BackfillFailureCount);
        Assert.Equal(BackfillStatus.FAILED, channel.BackfillStatus);
        Assert.False(channel.IsBackfillEligible);
        Assert.False(channel.TryClaimPage(Guid.NewGuid(), Now.AddHours(1)));
    }

    [Fact]
    public void The_failure_count_stops_at_the_configured_limit()
    {
        var channel = ClaimedChannel(out var claimToken);
        channel.BackfillFailureCount = Channel.BackfillFailureLimit - 1;

        Assert.True(channel.RecordBackfillFailure(claimToken, Reason, Now));

        Assert.Equal(Channel.BackfillFailureLimit, channel.BackfillFailureCount);
        Assert.Equal(BackfillStatus.FAILED, channel.BackfillStatus);
        Assert.False(channel.IsBackfillEligible);
    }

    [Fact]
    public void A_failure_records_the_safe_reason_and_timestamp_and_releases_the_owned_claim()
    {
        var channel = ClaimedChannel(out var claimToken);

        Assert.True(channel.RecordBackfillFailure(claimToken, $"  {Reason}  ", Now));

        Assert.Equal(Reason, channel.BackfillFailureReason);
        Assert.Equal(Now, channel.BackfillLastFailedAt);
        Assert.Equal(Now, channel.ModifiedAt);
        Assert.Null(channel.BackfillClaimToken);
        Assert.Null(channel.BackfillClaimedAt);
    }

    [Fact]
    public void Releasing_a_claim_without_counting_a_failure_keeps_the_channel_eligible()
    {
        var channel = ClaimedChannel(out var claimToken);

        Assert.True(channel.TryDeferBackfillForQuota(claimToken, Now));

        Assert.Equal(0, channel.BackfillFailureCount);
        Assert.Equal(BackfillStatus.PENDING, channel.BackfillStatus);
        Assert.True(channel.IsBackfillEligible);
    }

    [Fact]
    public void A_successful_page_reset_clears_every_persisted_failure_field()
    {
        var channel = ClaimedChannel(out var claimToken);
        Assert.True(channel.RecordBackfillFailure(claimToken, Reason, Now));

        channel.ResetBackfillFailure(Now.AddMinutes(30));

        Assert.Equal(0, channel.BackfillFailureCount);
        Assert.Null(channel.BackfillFailureReason);
        Assert.Null(channel.BackfillLastFailedAt);
    }

    [Fact]
    public void An_overlong_failure_reason_is_rejected_before_any_state_change()
    {
        var channel = ClaimedChannel(out var claimToken);
        var reason = new string('a', Channel.BackfillFailureReasonMaxLength + 1);

        var exception = Assert.Throws<ArgumentException>(
            () => channel.RecordBackfillFailure(claimToken, reason, Now));

        Assert.Contains("cannot exceed", exception.Message, StringComparison.Ordinal);
        Assert.Equal(0, channel.BackfillFailureCount);
        Assert.Equal(claimToken, channel.BackfillClaimToken);
    }

    private static Channel ClaimedChannel(out Guid claimToken)
    {
        claimToken = Guid.NewGuid();

        var channel = new Channel
        {
            Id = Guid.NewGuid(),
            YoutubeChannelId = "UC-backfill-failure",
            Name = "Backfill failure channel",
            Status = ChannelStatus.APPROVED,
            ApprovedAt = Now.AddDays(-1),
            BackfillStatus = BackfillStatus.PENDING,
            IsDeleted = false,
            DiscoveredAt = Now.AddDays(-2),
            CreatedAt = Now.AddDays(-2),
            ModifiedAt = Now.AddDays(-1)
        };

        Assert.True(channel.TryClaimPage(claimToken, Now));
        return channel;
    }
}
