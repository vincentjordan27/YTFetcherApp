using YtFetcher.Core.Channels;

namespace YtFetcher.Api.Tests.Core;

/// <summary>
/// Claim, cutoff, and cursor rules for the M5 approved-channel backfill.
/// </summary>
public class ChannelBackfillDomainTests
{
    private static readonly DateTimeOffset Now = new(2026, 9, 29, 4, 0, 0, TimeSpan.Zero);

    [Fact]
    public void Approved_non_completed_channel_is_eligible_and_can_be_claimed()
    {
        var channel = EligibleChannel();
        var claimToken = Guid.NewGuid();

        Assert.True(channel.IsBackfillEligible);
        Assert.True(channel.TryClaimPage(claimToken, Now));
        Assert.Equal(claimToken, channel.BackfillClaimToken);
        Assert.Equal(Now, channel.BackfillClaimedAt);
        Assert.Equal(Now, channel.ModifiedAt);
    }

    [Fact]
    public void A_deleted_channel_is_never_eligible()
    {
        var channel = EligibleChannel();
        channel.SoftDelete(Now.AddDays(-1), null);

        Assert.False(channel.IsBackfillEligible);
        Assert.False(channel.TryClaimPage(Guid.NewGuid(), Now));
    }

    [Fact]
    public void Only_the_claim_owner_can_release_the_claim()
    {
        var channel = EligibleChannel();
        var owner = Guid.NewGuid();
        Assert.True(channel.TryClaimPage(owner, Now));

        Assert.False(channel.TryDeferBackfillForQuota(Guid.NewGuid(), Now));
        Assert.Equal(owner, channel.BackfillClaimToken);

        Assert.True(channel.TryDeferBackfillForQuota(owner, Now.AddMinutes(1)));
        Assert.Null(channel.BackfillClaimToken);
        Assert.Null(channel.BackfillClaimedAt);
    }

    [Fact]
    public void Cutoff_is_frozen_once_and_later_pages_reuse_it()
    {
        var channel = EligibleChannel();

        channel.FreezeBackfillCutoff(Now);
        var frozen = Now.AddDays(-730);
        Assert.Equal(frozen, channel.BackfillCutoffAt);

        channel.FreezeBackfillCutoff(Now.AddDays(3));
        Assert.Equal(frozen, channel.BackfillCutoffAt);
    }

    private static Channel EligibleChannel() => new()
    {
        Id = Guid.NewGuid(),
        YoutubeChannelId = "UC-backfill-domain",
        Name = "Backfill domain channel",
        Status = ChannelStatus.APPROVED,
        ApprovedAt = Now.AddDays(-1),
        BackfillStatus = BackfillStatus.PENDING,
        IsDeleted = false,
        DiscoveredAt = Now.AddDays(-2),
        CreatedAt = Now.AddDays(-2),
        ModifiedAt = Now.AddDays(-1)
    };
}
