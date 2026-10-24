using YtFetcher.Core.Quotas;

namespace YtFetcher.Api.Tests.Core;

/// <summary>
/// The daily-process claim is the durable owner of one Pacific quota date. These tests pin the decision rules
/// that keep a retry, a concurrent trigger, or a crash from opening backfill twice.
/// </summary>
public class DailyProcessingOwnershipTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 6, 8, 0, 0, TimeSpan.Zero);

    [Fact]
    public void An_unclaimed_date_is_claimed_by_the_first_attempt()
    {
        var ledger = NewLedger();
        var claimToken = Guid.NewGuid();

        var decision = ledger.TryClaimDailyProcessing(claimToken, Now);

        Assert.Equal(DailyProcessClaimDecision.Claimed, decision);
        Assert.Equal(claimToken, ledger.DailyProcessingClaimToken);
        Assert.Equal(Now, ledger.DailyProcessingClaimedAt);
    }

    [Fact]
    public void A_live_claim_is_never_stolen()
    {
        var ledger = NewLedger();
        var firstToken = Guid.NewGuid();
        ledger.TryClaimDailyProcessing(firstToken, Now);

        // One hour later the owner's two-hour lifetime is still running.
        var decision = ledger.TryClaimDailyProcessing(Guid.NewGuid(), Now.AddHours(1));

        Assert.Equal(DailyProcessClaimDecision.AlreadyRunning, decision);
        Assert.Equal(firstToken, ledger.DailyProcessingClaimToken);
        Assert.Equal(Now, ledger.DailyProcessingClaimedAt);
    }

    [Fact]
    public void A_claim_is_stale_at_exactly_two_hours_and_can_be_recovered()
    {
        var ledger = NewLedger();
        ledger.TryClaimDailyProcessing(Guid.NewGuid(), Now);

        // One tick before the boundary the claim is still live and must not be stolen.
        Assert.Equal(
            DailyProcessClaimDecision.AlreadyRunning,
            ledger.TryClaimDailyProcessing(Guid.NewGuid(), Now.Add(YouTubeDailyQuotaUsage.DailyProcessClaimLifetime - TimeSpan.FromTicks(1))));

        // At exactly two hours the claim has expired, which is the recovery boundary a crashed host relies on.
        var recoveryToken = Guid.NewGuid();
        var recovered = ledger.TryClaimDailyProcessing(
            recoveryToken,
            Now.Add(YouTubeDailyQuotaUsage.DailyProcessClaimLifetime));

        Assert.Equal(DailyProcessClaimDecision.Claimed, recovered);
        Assert.Equal(recoveryToken, ledger.DailyProcessingClaimToken);
    }

    [Fact]
    public void A_handled_release_frees_the_date_without_completing_it()
    {
        var ledger = NewLedger();
        ledger.TryClaimDailyProcessing(Guid.NewGuid(), Now);

        ledger.ReleaseDailyProcessingClaim();

        Assert.Null(ledger.DailyProcessingClaimToken);
        Assert.Null(ledger.DailyProcessingClaimedAt);
        Assert.False(ledger.IsDailyProcessingCompleted);
        Assert.Equal(DailyProcessClaimDecision.Claimed, ledger.TryClaimDailyProcessing(Guid.NewGuid(), Now.AddMinutes(1)));
    }

    [Fact]
    public void Completion_is_written_once_and_blocks_every_later_attempt()
    {
        var ledger = NewLedger();
        ledger.TryClaimDailyProcessing(Guid.NewGuid(), Now);

        ledger.CompleteDailyProcessing(Now);
        var firstCompletion = ledger.DailyProcessingCompletedAt;

        // A repeated completion must not move the recorded instant.
        ledger.CompleteDailyProcessing(Now.AddHours(3));

        Assert.Equal(firstCompletion, ledger.DailyProcessingCompletedAt);
        Assert.Null(ledger.DailyProcessingClaimToken);
        Assert.Equal(DailyProcessClaimDecision.AlreadyCompleted, ledger.TryClaimDailyProcessing(Guid.NewGuid(), Now.AddHours(4)));
    }

    [Fact]
    public void Each_bucket_exhaustion_marker_is_written_once_independently()
    {
        var ledger = NewLedger();
        ledger.TryClaimDailyProcessing(Guid.NewGuid(), Now);

        ledger.MarkQueriesQuotaExhausted(Now);
        // A repeated mark must not move the recorded instant.
        ledger.MarkQueriesQuotaExhausted(Now.AddHours(1));
        ledger.MarkVideoBatchStatsQuotaExhausted(Now.AddMinutes(1));

        Assert.Equal(Now, ledger.QueriesQuotaExhaustedAt);
        Assert.True(ledger.IsQueriesQuotaExhausted);
        Assert.Equal(Now.AddMinutes(1), ledger.VideoBatchStatsQuotaExhaustedAt);
        Assert.True(ledger.IsVideoBatchStatsQuotaExhausted);

        // Buckets are independent: Search stays available and the date is not completed by a limit.
        Assert.False(ledger.IsSearchQuotaExhausted);
        Assert.False(ledger.IsDailyProcessingCompleted);
    }

    [Fact]
    public void The_first_page_window_is_not_ready_until_the_limit_is_snapshotted()
    {
        var ledger = NewLedger();
        Assert.False(ledger.IsFirstPageWindowReady);

        ledger.FirstPageAttemptLimit = 500;
        ledger.FirstPageAttemptUsed = 12;

        Assert.True(ledger.IsFirstPageWindowReady);
        Assert.Equal(488, ledger.UnclaimedFirstPageSlots);
    }

    private static YouTubeDailyQuotaUsage NewLedger() => new()
    {
        Id = Guid.NewGuid(),
        QuotaDate = DateOnly.FromDateTime(Now.UtcDateTime),
        CreatedAt = Now,
        ModifiedAt = Now
    };
}
