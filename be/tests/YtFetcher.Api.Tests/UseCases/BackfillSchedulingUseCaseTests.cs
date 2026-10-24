using YtFetcher.Core.BackfillRuns;
using YtFetcher.Application.Abstractions.Repositories;
using YtFetcher.Application.Abstractions.Services;
using YtFetcher.Application.Backfill.UseCases;
using YtFetcher.Application.Channels.DTO.Result;
using YtFetcher.Core.Channels;
using YtFetcher.Core.Discovery;

namespace YtFetcher.Api.Tests.UseCases;

/// <summary>
/// Enqueue success/failure handling for one scheduling attempt. The enqueue call happens outside any
/// database transaction and every outcome is written back in its own short mutation.
/// </summary>
public class BackfillSchedulingUseCaseTests
{
    private static readonly DateTimeOffset Now = new(2026, 9, 30, 10, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task Enqueue_success_leaves_the_row_durable_in_scheduling_without_log_noise()
    {
        var repository = new FakeChannelRepository();
        var scheduler = new FakeBackfillScheduler();
        var logger = new FakeBackfillActivityLogger();
        var useCase = new BackfillSchedulingUseCase(repository, scheduler, logger, new FixedTimeProvider(Now));
        var channelId = Guid.NewGuid();

        await useCase.ScheduleAsync(channelId, BackfillTriggerSource.APPROVAL, CancellationToken.None);

        Assert.Equal([channelId], scheduler.EnqueuedChannelIds);

        // No scheduling-state write follows a successful enqueue: the row stays SCHEDULING so the enqueued
        // worker performs the SCHEDULING -> RUNNING claim itself and a process death cannot strand it.
        Assert.Equal(0, repository.CompleteSchedulingCount);
        Assert.Null(repository.LastCompletionUtcNow);
        Assert.Equal(0, repository.FailSchedulingCount);
        Assert.Empty(logger.Events);
    }

    [Fact]
    public async Task Enqueue_exception_persists_the_safe_reason_and_logs_the_original_exception()
    {
        var repository = new FakeChannelRepository();
        var exception = new InvalidOperationException("scheduler unavailable");
        var scheduler = new FakeBackfillScheduler { Exception = exception };
        var logger = new FakeBackfillActivityLogger();
        var useCase = new BackfillSchedulingUseCase(repository, scheduler, logger, new FixedTimeProvider(Now));
        var channelId = Guid.NewGuid();

        await useCase.ScheduleAsync(channelId, BackfillTriggerSource.APPROVAL, CancellationToken.None);

        Assert.Equal(0, repository.CompleteSchedulingCount);
        Assert.Equal(1, repository.FailSchedulingCount);
        Assert.Equal(BackfillSchedulingUseCase.SafeSchedulingFailureReason, repository.LastFailureReason);
        Assert.Equal(Now, repository.LastFailureUtcNow);
        Assert.Equal(["SchedulingFailed"], logger.Events);
        Assert.Same(exception, logger.LastException);
    }

    [Fact]
    public async Task Enqueue_exception_followed_by_a_state_write_exception_is_recoverable_and_does_not_throw()
    {
        var repository = new FakeChannelRepository { FailSchedulingException = new InvalidOperationException("write failed") };
        var scheduler = new FakeBackfillScheduler { Exception = new InvalidOperationException("scheduler unavailable") };
        var logger = new FakeBackfillActivityLogger();
        var useCase = new BackfillSchedulingUseCase(repository, scheduler, logger, new FixedTimeProvider(Now));

        // The approval must remain durable as SCHEDULING: nothing is thrown and no failure state is
        // fabricated.
        await useCase.ScheduleAsync(Guid.NewGuid(), BackfillTriggerSource.APPROVAL, CancellationToken.None);

        Assert.Equal(["SchedulingFailed", "SchedulingStateWriteFailed"], logger.Events);
    }

    [Fact]
    public async Task Enqueue_success_never_writes_the_forbidden_pending_transition()
    {
        // The baseline used to perform a completion write after a successful enqueue. That write is gone by
        // design, so this pins its absence rather than the exception path it used to exercise.
        var repository = new FakeChannelRepository();
        var scheduler = new FakeBackfillScheduler();
        var logger = new FakeBackfillActivityLogger();
        var useCase = new BackfillSchedulingUseCase(repository, scheduler, logger, new FixedTimeProvider(Now));

        await useCase.ScheduleAsync(Guid.NewGuid(), BackfillTriggerSource.APPROVAL, CancellationToken.None);

        Assert.Equal(0, repository.CompleteSchedulingCount);
        Assert.Equal(0, repository.FailSchedulingCount);
        Assert.Empty(logger.Events);
    }

    private sealed class FakeBackfillScheduler : IBackfillScheduler
    {
        public List<Guid> EnqueuedChannelIds { get; } = [];

        public Exception? Exception { get; init; }

        public void EnqueuePage(Guid channelId, BackfillTriggerSource triggerSource)
        {
            EnqueuedChannelIds.Add(channelId);

            if (Exception is not null)
            {
                throw Exception;
            }
        }
    }

    private sealed class FakeBackfillActivityLogger : IBackfillActivityLogger
    {        public List<string> Events { get; } = [];
        public Exception? LastException { get; private set; }
        public void LogFirstPageProviderFailed(Guid channelId, string operation, string reason) =>
            Events.Add($"FirstPageProviderFailed:{operation}:{reason}");
        public void LogFirstPageFailed(Guid channelId, int failureCount, int failureLimit, string reason) =>
            Events.Add($"FirstPageFailed:{failureCount}");
        public void LogFirstPageQuotaExceeded(Guid channelId, DateOnly quotaDate, string operation) =>
            Events.Add($"FirstPageQuotaExceeded:{operation}");
        public void LogFailureStateWriteFailed(Guid channelId, string operation, Exception exception) =>
            Events.Add($"FailureStateWriteFailed:{operation}");
        public void LogPersistenceFailed(Guid channelId, string operation, Exception exception) =>
            Events.Add($"PersistenceFailed:{operation}");
        public void LogSchedulingFailed(Guid channelId, Exception exception)
        {
            LastException = exception;
            Events.Add("SchedulingFailed");
        }
        public void LogSchedulingStateWriteFailed(Guid channelId, Exception exception) =>
            Events.Add("SchedulingStateWriteFailed");
        public void LogTerminalPageFailure(Guid channelId, int failureCount, string reason) =>
            Events.Add($"TerminalPageFailure:{failureCount}");
        public void LogRetentionCleared(int deletedCount, int batchCount) =>
            Events.Add($"RetentionCleared:{deletedCount}:{batchCount}");
    }

    private sealed class FakeChannelRepository : IChannelRepository
    {
        public int CompleteSchedulingCount { get; private set; }

        public int FailSchedulingCount { get; private set; }

        public DateTimeOffset? LastCompletionUtcNow { get; private set; }

        public DateTimeOffset? LastFailureUtcNow { get; private set; }

        public string? LastFailureReason { get; private set; }

        public Exception? CompleteSchedulingException { get; init; }

        public Exception? FailSchedulingException { get; init; }

        public Task<Channel?> TryCompleteSchedulingAsync(
            Guid channelId,
            DateTimeOffset utcNow,
            CancellationToken cancellationToken)
        {
            CompleteSchedulingCount++;
            LastCompletionUtcNow = utcNow;

            return CompleteSchedulingException is not null
                ? Task.FromException<Channel?>(CompleteSchedulingException)
                : Task.FromResult<Channel?>(null);
        }

        public Task<Channel?> TryFailSchedulingAsync(
            Guid channelId,
            string safeReason,
            DateTimeOffset utcNow,
            CancellationToken cancellationToken)
        {
            FailSchedulingCount++;
            LastFailureReason = safeReason;
            LastFailureUtcNow = utcNow;

            return FailSchedulingException is not null
                ? Task.FromException<Channel?>(FailSchedulingException)
                : Task.FromResult<Channel?>(null);
        }

        public Task<Channel?> GetActiveAsync(Guid channelId, CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public Task<Channel?> GetActiveForUpdateAsync(Guid channelId, CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public Task<ManualChannelCreateResult> CreateApprovedForSchedulingAsync(
            Channel channel,
            CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public Task<Channel?> TryApproveForSchedulingAsync(
            Guid channelId,
            DateTimeOffset utcNow,
            CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public Task<Channel?> TryRetryFailedSchedulingAsync(
            Guid channelId,
            DateTimeOffset utcNow,
            CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public Task<IReadOnlyList<Guid>> LeaseStaleSchedulingAsync(
            DateTimeOffset staleBeforeUtc,
            DateTimeOffset utcNow,
            int maximumCount,
            CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public Task<IReadOnlyList<Channel>> GetPendingPageAsync(
            int page,
            int pageSize,
            string? searchText,
            CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public Task<int> CountPendingAsync(string? searchText, CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public Task<IReadOnlyList<Channel>> GetReviewPageAsync(
            ChannelStatus status,
            int page,
            int pageSize,
            string? searchText,
            CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public Task<int> CountReviewChannelsAsync(
            ChannelStatus status,
            string? searchText,
            CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public Task<IReadOnlyDictionary<ChannelStatus, int>> CountChannelsByStatusAsync(
            CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public Task<IReadOnlyDictionary<Guid, int>> CountEvidenceAsync(
            IReadOnlyCollection<Guid> channelIds,
            CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public Task<IReadOnlyList<ChannelDiscovery>> GetEvidenceAsync(
            Guid channelId,
            int limit,
            CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public Task<IReadOnlyDictionary<Guid, DiscoveryKeyword>> GetDiscoveryKeywordsByIdsAsync(
            IReadOnlyCollection<Guid> keywordIds,
            CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public Task SaveChangesAsync(CancellationToken cancellationToken) =>
            throw new NotSupportedException();
        public Task<ChannelApprovalResult> TryApproveForSchedulingAsync(Guid channelId, DateOnly quotaDate, Guid approvedByUserId, DateTimeOffset utcNow, CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public Task<ChannelBatchMutationResult> TryApproveManyForSchedulingAsync(IReadOnlyList<Guid> channelIds, DateOnly quotaDate, Guid approvedByUserId, DateTimeOffset utcNow, CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public Task<ChannelBatchMutationResult> TryRejectManyAsync(IReadOnlyList<Guid> channelIds, Guid rejectedByUserId, string reason, DateTimeOffset utcNow, CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public Task<ManualChannelCreateResult> CreateApprovedForSchedulingAsync(Channel channel, DateOnly quotaDate, CancellationToken cancellationToken) =>
            throw new NotSupportedException();
}

    private sealed class FixedTimeProvider(DateTimeOffset utcNow) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => utcNow;
    }
}
