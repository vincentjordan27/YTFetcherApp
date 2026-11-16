using Hangfire;
using YtFetcher.Core.BackfillRuns;
using YtFetcher.Api.Tests.Channels;
using YtFetcher.Application.Abstractions.UseCases;
using YtFetcher.Application.Backfill.DTO.Result;
using YtFetcher.Infrastructure.Jobs;

namespace YtFetcher.Api.Tests.Backfill;

/// <summary>
/// The Hangfire adapter invokes one page and does not swallow failures.
/// </summary>
public class ChannelBackfillHangfireJobTests
{
    [Fact]
    public void Job_retries_twice_with_fifteen_minute_delays()
    {
        var attribute = typeof(ChannelBackfillHangfireJob)
            .GetMethod(nameof(ChannelBackfillHangfireJob.RunPageAsync))!
            .GetCustomAttributes(typeof(AutomaticRetryAttribute), inherit: false)
            .Cast<AutomaticRetryAttribute>()
            .Single();

        Assert.Equal(2, attribute.Attempts);
        Assert.NotNull(attribute.DelaysInSeconds);
        Assert.Equal(new[] { 900, 900 }, attribute.DelaysInSeconds);
    }

    [Fact]
    public async Task Job_invokes_the_use_case_once_for_the_channel()
    {
        var useCase = new FakeBackfillUseCase();
        var job = new ChannelBackfillHangfireJob(useCase);
        var channelId = Guid.NewGuid();

        await job.RunPageAsync(channelId, BackfillTriggerSource.APPROVAL, CancellationToken.None);

        Assert.Equal([channelId], useCase.ReceivedChannelIds);
    }

    [Fact]
    public async Task Job_does_not_swallow_an_unexpected_use_case_exception()
    {
        var useCase = new FakeBackfillUseCase(new InvalidOperationException("infrastructure down"));
        var job = new ChannelBackfillHangfireJob(useCase);

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => job.RunPageAsync(Guid.NewGuid(), BackfillTriggerSource.APPROVAL, CancellationToken.None));

        Assert.Equal(1, useCase.CallCount);
    }

    [Fact]
    public void Scheduler_enqueues_the_backfill_job_for_the_approved_channel()
    {
        var client = new RecordingBackgroundJobClient();
        var scheduler = new HangfireBackfillScheduler(client);
        var channelId = Guid.NewGuid();

        scheduler.EnqueuePage(channelId, BackfillTriggerSource.APPROVAL);

        var job = Assert.Single(client.CreatedJobs);
        Assert.Equal(typeof(ChannelBackfillHangfireJob), job.Type);
        Assert.Equal(nameof(ChannelBackfillHangfireJob.RunPageAsync), job.Method.Name);
        Assert.Equal(channelId, job.Args[0]);
    }

    private sealed class FakeBackfillUseCase(
        Exception? exception = null,
        BackfillPageExecutionResult? outcome = null) : IBackfillUseCase
    {
        public List<Guid> ReceivedChannelIds { get; } = [];

        public int CallCount => ReceivedChannelIds.Count;

        public Task<BackfillPageExecutionResult> RunPageAsync(
            Guid channelId,
            BackfillTriggerSource triggerSource,
            CancellationToken cancellationToken)
        {
            ReceivedChannelIds.Add(channelId);

            if (exception is not null)
            {
                return Task.FromException<BackfillPageExecutionResult>(exception);
            }

            return Task.FromResult(outcome ?? BackfillPageExecutionResult.Stopped);
        }
    }
}
