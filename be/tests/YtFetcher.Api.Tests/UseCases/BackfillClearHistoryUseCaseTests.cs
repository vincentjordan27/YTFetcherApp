using YtFetcher.Api.Tests.BackfillRuns;
using YtFetcher.Application.Abstractions.Services;
using YtFetcher.Core.Channels;
using YtFetcher.Application.Backfill.UseCases;
using YtFetcher.Core.BackfillRuns;

namespace YtFetcher.Api.Tests.UseCases;

/// <summary>
/// Backfill run retention: only terminal rows strictly older than the retention window are physically
/// removed, running rows are never touched, and one invocation can never run past its batch bound.
/// </summary>
public class BackfillClearHistoryUseCaseTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 5, 4, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task Only_terminal_rows_older_than_the_window_are_removed()
    {
        var repository = new InMemoryBackfillRunRepository();
        var expired = SeedTerminal(repository, Now.AddDays(-(BackfillClearHistoryUseCase.RetentionDays + 1)));
        var running = SeedRunning(repository, Now.AddDays(-(BackfillClearHistoryUseCase.RetentionDays + 1)));
        var softDeleted = SeedTerminal(repository, Now.AddDays(-(BackfillClearHistoryUseCase.RetentionDays + 1)));
        softDeleted.IsDeleted = true;
        var recent = SeedTerminal(repository, Now.AddDays(-10));

        var result = await CreateUseCase(repository).RunAsync(CancellationToken.None);

        Assert.Equal(1, result.DeletedCount);
        Assert.False(result.ReachedBatchLimit);
        Assert.DoesNotContain(expired, repository.All);
        Assert.Contains(running, repository.All);
        Assert.Contains(softDeleted, repository.All);
        Assert.Contains(recent, repository.All);
    }

    [Fact]
    public async Task The_retention_boundary_is_strictly_older_than_the_window()
    {
        var repository = new InMemoryBackfillRunRepository();
        var atBoundary = SeedTerminal(repository, Now.AddDays(-BackfillClearHistoryUseCase.RetentionDays));
        var justOlder = SeedTerminal(
            repository,
            Now.AddDays(-BackfillClearHistoryUseCase.RetentionDays).AddSeconds(-1));

        var result = await CreateUseCase(repository).RunAsync(CancellationToken.None);

        Assert.Equal(1, result.DeletedCount);
        Assert.Contains(atBoundary, repository.All);
        Assert.DoesNotContain(justOlder, repository.All);
    }

    [Fact]
    public async Task A_backlog_stops_at_the_batch_limit_and_reports_that_more_may_remain()
    {
        var repository = new InMemoryBackfillRunRepository();
        var totalExpired = (BackfillClearHistoryUseCase.MaximumBatches * BackfillClearHistoryUseCase.BatchSize) + 1;

        for (var index = 0; index < totalExpired; index++)
        {
            SeedTerminal(repository, Now.AddDays(-(BackfillClearHistoryUseCase.RetentionDays + 1)));
        }

        var result = await CreateUseCase(repository).RunAsync(CancellationToken.None);

        Assert.Equal(
            BackfillClearHistoryUseCase.MaximumBatches * BackfillClearHistoryUseCase.BatchSize,
            result.DeletedCount);
        Assert.Equal(BackfillClearHistoryUseCase.MaximumBatches, result.BatchCount);
        Assert.True(result.ReachedBatchLimit);
        Assert.Equal(BackfillClearHistoryUseCase.MaximumBatches, repository.RetentionDeleteCallCount);
        Assert.Single(repository.All);
    }

    [Fact]
    public async Task An_empty_history_runs_one_short_batch_and_reports_nothing_removed()
    {
        var repository = new InMemoryBackfillRunRepository();
        var logger = new RecordingBackfillActivityLogger();

        var result = await new BackfillClearHistoryUseCase(repository, logger, new FixedTimeProvider(Now))
            .RunAsync(CancellationToken.None);

        Assert.Equal(0, result.DeletedCount);
        Assert.Equal(1, result.BatchCount);
        Assert.False(result.ReachedBatchLimit);

        // An empty pass removed nothing, so it must stay silent instead of logging a zero-value event.
        Assert.Empty(logger.Events);
    }

    [Fact]
    public async Task A_pass_logs_the_cleared_count_and_the_batch_count()
    {
        var repository = new InMemoryBackfillRunRepository();
        SeedTerminal(repository, Now.AddDays(-(BackfillClearHistoryUseCase.RetentionDays + 1)));
        var logger = new RecordingBackfillActivityLogger();

        var result = await new BackfillClearHistoryUseCase(repository, logger, new FixedTimeProvider(Now))
            .RunAsync(CancellationToken.None);

        Assert.Equal(1, result.DeletedCount);
        // Exactly one aggregate event for a real deletion, never one per row.
        Assert.Equal(["RetentionCleared:1:1"], logger.Events);
    }

    private static BackfillClearHistoryUseCase CreateUseCase(InMemoryBackfillRunRepository repository) =>
        new(repository, new RecordingBackfillActivityLogger(), new FixedTimeProvider(Now));

    private static ChannelBackfillRun SeedTerminal(
        InMemoryBackfillRunRepository repository,
        DateTimeOffset startedAt)
    {
        var run = SeedRunning(repository, startedAt);
        run.Succeed(startedAt.AddMinutes(1));
        return run;
    }

    private static ChannelBackfillRun SeedRunning(
        InMemoryBackfillRunRepository repository,
        DateTimeOffset startedAt)
    {
        var run = ChannelBackfillRun.Start(
            Guid.NewGuid(),
            Guid.NewGuid(),
            BackfillTriggerSource.DAILY_PUMP,
            1,
            startedAt);

        repository.SeedRun(run);
        return run;
    }

    private sealed class RecordingBackfillActivityLogger : IBackfillActivityLogger
    {        public List<string> Events { get; } = [];
        public void LogRetentionCleared(int deletedCount, int batchCount) =>
            Events.Add($"RetentionCleared:{deletedCount}:{batchCount}");
        public void LogFirstPageProviderFailed(Guid channelId, string operation, string reason) =>
            Events.Add("FirstPageProviderFailed");
        public void LogFirstPageFailed(Guid channelId, int failureCount, int failureLimit, string reason) =>
            Events.Add("FirstPageFailed");
        public void LogFirstPageQuotaExceeded(Guid channelId, DateOnly quotaDate, string operation) =>
            Events.Add("FirstPageQuotaExceeded");
        public void LogFailureStateWriteFailed(Guid channelId, string operation, Exception exception) =>
            Events.Add("FailureStateWriteFailed");
        public void LogPersistenceFailed(Guid channelId, string operation, Exception exception) =>
            Events.Add("PersistenceFailed");
        public void LogSchedulingFailed(Guid channelId, Exception exception) => Events.Add("SchedulingFailed");
        public void LogSchedulingStateWriteFailed(Guid channelId, Exception exception) =>
            Events.Add("SchedulingStateWriteFailed");
        public void LogTerminalPageFailure(Guid channelId, int failureCount, string reason) =>
            Events.Add("TerminalPageFailure");
    }

    private sealed class FixedTimeProvider(DateTimeOffset utcNow) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => utcNow;
    }
}
