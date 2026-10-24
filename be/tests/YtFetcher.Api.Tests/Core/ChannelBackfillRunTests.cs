using YtFetcher.Core.BackfillRuns;

namespace YtFetcher.Api.Tests.Core;

/// <summary>
/// Durable backfill run history: one row per claimed page attempt, closed exactly once as succeeded,
/// failed, or abandoned. The row must never carry technical diagnostics.
/// </summary>
public class ChannelBackfillRunTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 5, 4, 0, 0, TimeSpan.Zero);

    [Fact]
    public void Start_opens_a_running_row_for_the_claimed_page()
    {
        var channelId = Guid.NewGuid();
        var claimToken = Guid.NewGuid();

        var run = ChannelBackfillRun.Start(channelId, claimToken, BackfillTriggerSource.DAILY_PUMP, 2, Now);

        Assert.NotEqual(Guid.Empty, run.Id);
        Assert.Equal(channelId, run.ChannelId);
        Assert.Equal(claimToken, run.ClaimToken);
        Assert.Equal(BackfillTriggerSource.DAILY_PUMP, run.TriggerSource);
        Assert.Equal(BackfillRunStatus.RUNNING, run.Status);
        Assert.Equal(2, run.PageNumber);
        Assert.Equal(Now, run.StartedAt);
        Assert.Equal(Now, run.CreatedAt);
        Assert.Equal(Now, run.ModifiedAt);
        Assert.Null(run.CompletedAt);
        Assert.Null(run.FailureReason);
        Assert.Null(run.FailureCountAtTime);
        Assert.False(run.IsDeleted);
    }

    [Theory]
    [InlineData(BackfillTriggerSource.APPROVAL)]
    [InlineData(BackfillTriggerSource.DAILY_PUMP)]
    [InlineData(BackfillTriggerSource.RECOVERY)]
    public void Every_trigger_source_round_trips_through_its_persisted_value(BackfillTriggerSource triggerSource)
    {
        var run = ChannelBackfillRun.Start(Guid.NewGuid(), Guid.NewGuid(), triggerSource, 1, Now);

        Assert.Equal(triggerSource.ToString(), run.TriggerSourceValue);
        Assert.Equal(triggerSource, run.TriggerSource);
    }

    [Fact]
    public void A_page_number_must_be_positive()
    {
        Assert.Throws<ArgumentOutOfRangeException>(
            () => ChannelBackfillRun.Start(Guid.NewGuid(), Guid.NewGuid(), BackfillTriggerSource.APPROVAL, 0, Now));
    }

    [Fact]
    public void Succeed_closes_a_running_row_once()
    {
        var run = ChannelBackfillRun.Start(Guid.NewGuid(), Guid.NewGuid(), BackfillTriggerSource.APPROVAL, 1, Now);

        Assert.True(run.Succeed(Now.AddMinutes(1)));

        Assert.Equal(BackfillRunStatus.SUCCEEDED, run.Status);
        Assert.Equal(Now.AddMinutes(1), run.CompletedAt);
        Assert.Equal(Now.AddMinutes(1), run.ModifiedAt);
        Assert.Null(run.FailureReason);
        Assert.Null(run.FailureCountAtTime);
        Assert.False(run.Succeed(Now.AddMinutes(2)));
    }

    [Fact]
    public void Fail_records_the_readable_reason_and_the_failure_count_at_the_time()
    {
        var run = ChannelBackfillRun.Start(Guid.NewGuid(), Guid.NewGuid(), BackfillTriggerSource.RECOVERY, 3, Now);

        Assert.True(run.Fail("Unable to save backfill results. Try again later.", 2, Now.AddMinutes(1)));

        Assert.Equal(BackfillRunStatus.FAILED, run.Status);
        Assert.Equal("Unable to save backfill results. Try again later.", run.FailureReason);
        Assert.Equal(2, run.FailureCountAtTime);
        Assert.Equal(Now.AddMinutes(1), run.CompletedAt);
        Assert.False(run.Fail("second attempt", 3, Now.AddMinutes(2)));
        Assert.Equal("Unable to save backfill results. Try again later.", run.FailureReason);
    }

    [Fact]
    public void A_failure_reason_is_trimmed_and_must_be_present()
    {
        var run = ChannelBackfillRun.Start(Guid.NewGuid(), Guid.NewGuid(), BackfillTriggerSource.APPROVAL, 1, Now);

        Assert.Throws<ArgumentException>(() => run.Fail("   ", 1, Now));
        Assert.Throws<ArgumentOutOfRangeException>(() => run.Fail("readable reason", -1, Now));
        Assert.Throws<ArgumentException>(
            () => run.Fail(new string('x', ChannelBackfillRun.FailureReasonMaxLength + 1), 1, Now));

        Assert.True(run.Fail("  Unable to retrieve uploads from YouTube. Try again later.  ", 1, Now));
        Assert.Equal("Unable to retrieve uploads from YouTube. Try again later.", run.FailureReason);
        Assert.Equal(BackfillRunStatus.FAILED, run.Status);
    }

    [Fact]
    public void Abandon_closes_the_row_without_failure_detail()
    {
        var run = ChannelBackfillRun.Start(Guid.NewGuid(), Guid.NewGuid(), BackfillTriggerSource.DAILY_PUMP, 1, Now);

        Assert.True(run.Abandon(Now.AddMinutes(30)));

        Assert.Equal(BackfillRunStatus.ABANDONED, run.Status);
        Assert.Equal(Now.AddMinutes(30), run.CompletedAt);
        Assert.Null(run.FailureReason);
        Assert.Null(run.FailureCountAtTime);
        Assert.False(run.Abandon(Now.AddMinutes(31)));
    }

    [Fact]
    public void A_terminal_row_cannot_change_its_terminal_state()
    {
        var abandoned = ChannelBackfillRun.Start(Guid.NewGuid(), Guid.NewGuid(), BackfillTriggerSource.APPROVAL, 1, Now);
        Assert.True(abandoned.Abandon(Now.AddMinutes(1)));

        Assert.False(abandoned.Succeed(Now.AddMinutes(2)));
        Assert.False(abandoned.Fail("a later worker failed", 3, Now.AddMinutes(2)));

        Assert.Equal(BackfillRunStatus.ABANDONED, abandoned.Status);
        Assert.Null(abandoned.FailureReason);
        Assert.Equal(Now.AddMinutes(1), abandoned.CompletedAt);
    }
}
