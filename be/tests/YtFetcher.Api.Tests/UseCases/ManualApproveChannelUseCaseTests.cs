using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using YtFetcher.Application.Abstractions.Repositories;
using YtFetcher.Application.Abstractions.Services;
using YtFetcher.Application.Abstractions.UseCases;
using YtFetcher.Application.Backfill.DTO.Result;
using YtFetcher.Application.Channels.DTO.Request;
using YtFetcher.Application.Channels.DTO.Response;
using YtFetcher.Application.Channels.DTO.Result;
using YtFetcher.Application.Channels.UseCases;
using YtFetcher.Application.Quotas;
using YtFetcher.Core.BackfillRuns;
using YtFetcher.Core.Channels;
using YtFetcher.Core.Quotas;
using YtFetcher.Application.Quotas.DTO.Result;
using YtFetcher.Core.Discovery;
using YtFetcher.Infrastructure.Persistence;
using YtFetcher.Infrastructure.Persistence.Repositories;

namespace YtFetcher.Api.Tests.UseCases;

/// <summary>
/// Manual add by <c>@handle</c>: one lookup, one atomic APPROVED + SCHEDULING create, then the
/// existing scheduling path. Duplicates are never revived or scheduled.
/// </summary>
public class ManualApproveChannelUseCaseTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 1, 18, 0, 0, TimeSpan.Zero);
    private static readonly Guid ActorUserId = Guid.NewGuid();

    [Fact]
    public async Task A_valid_handle_creates_approved_scheduling_then_schedules_after_the_create()
    {
        var gateway = new FakeLookupGateway
        {
            Result = ManualChannelLookupResult.Found(
                "UC-manual",
                "Manual Channel",
                "https://example.com/manual.jpg",
                "UU-manual")
        };
        var repository = new FakeRepository();
        var scheduling = new FakeScheduling(repository);
        var useCase = Create(gateway, repository, scheduling);

        var result = await useCase.ManualApproveAsync(
            new ManualApproveChannelRequest("  @manualchannel  "),
            CancellationToken.None);

        Assert.Equal(ChannelMutationStatus.Succeeded, result.Status);
        Assert.NotNull(result.Channel);
        Assert.Equal("UC-manual", result.Channel.YoutubeChannelId);
        Assert.Equal("Manual Channel", result.Channel.Name);
        Assert.Equal("UU-manual", result.Channel.UploadsPlaylistId);
        Assert.Equal(ChannelStatus.APPROVED.ToString(), result.Channel.Status);
        Assert.Equal(BackfillStatus.PENDING.ToString(), result.Channel.BackfillStatus);
        Assert.Equal(ActorUserId, repository.CreatedChannel!.CreatedByUserId);
        Assert.Equal(ActorUserId, repository.CreatedChannel.ApprovedByUserId);
        Assert.Equal(ActorUserId, repository.CreatedChannel.ModifiedByUserId);

        // The handle is trimmed before the single lookup; the gateway is never called twice.
        Assert.Equal(["@manualchannel"], gateway.ReceivedHandles);
        Assert.Equal(1, repository.CreateCount);

        // Scheduling runs once, and only after the create committed the channel.
        Assert.Equal([repository.CreatedChannel!.Id], scheduling.ScheduledChannelIds);
        Assert.Equal([BackfillTriggerSource.APPROVAL], scheduling.TriggerSources);
        Assert.True(scheduling.ChannelExistedWhenScheduled);
    }

    [Theory]
    [InlineData("https://www.youtube.com/@example")]
    [InlineData("www.youtube.com/@example")]
    [InlineData("UC1234567890")]
    [InlineData("example")]
    [InlineData("@")]
    [InlineData("@two words")]
    [InlineData("@slash/handle")]
    [InlineData("@query?x=1")]
    [InlineData("@fragment#x")]
    [InlineData("@double@@handle")]
    [InlineData("   ")]
    public async Task An_unsupported_reference_is_rejected_without_lookup_or_persistence(string reference)
    {
        var gateway = new FakeLookupGateway();
        var repository = new FakeRepository();
        var scheduling = new FakeScheduling(repository);
        var useCase = Create(gateway, repository, scheduling);

        var result = await useCase.ManualApproveAsync(
            new ManualApproveChannelRequest(reference),
            CancellationToken.None);

        Assert.Equal(ChannelMutationStatus.ValidationError, result.Status);
        Assert.Equal("Enter a YouTube handle starting with @.", result.ValidationMessage);
        Assert.Empty(gateway.ReceivedHandles);
        Assert.Equal(0, repository.CreateCount);
        Assert.Empty(scheduling.ScheduledChannelIds);
    }

    [Fact]
    public async Task A_provider_not_found_returns_not_found_without_persisting()
    {
        var gateway = new FakeLookupGateway { Result = ManualChannelLookupResult.NotFound() };
        var repository = new FakeRepository();
        var scheduling = new FakeScheduling(repository);
        var useCase = Create(gateway, repository, scheduling);

        var result = await useCase.ManualApproveAsync(
            new ManualApproveChannelRequest("@missing"),
            CancellationToken.None);

        Assert.Equal(ChannelMutationStatus.NotFound, result.Status);
        Assert.Null(result.Channel);
        Assert.Equal(0, repository.CreateCount);
        Assert.Empty(scheduling.ScheduledChannelIds);
        Assert.Equal(["@missing"], gateway.ReceivedHandles);
    }

    [Fact]
    public async Task A_provider_failure_returns_provider_unavailable_without_persisting()
    {
        var gateway = new FakeLookupGateway { Result = ManualChannelLookupResult.Unavailable() };
        var repository = new FakeRepository();
        var scheduling = new FakeScheduling(repository);
        var useCase = Create(gateway, repository, scheduling);

        var result = await useCase.ManualApproveAsync(
            new ManualApproveChannelRequest("@flaky"),
            CancellationToken.None);

        Assert.Equal(ChannelMutationStatus.ProviderUnavailable, result.Status);
        Assert.Null(result.Channel);
        Assert.Equal(0, repository.CreateCount);
        Assert.Empty(scheduling.ScheduledChannelIds);
    }

    [Fact]
    public async Task A_found_channel_without_an_uploads_playlist_is_not_persisted()
    {
        var gateway = new FakeLookupGateway
        {
            Result = ManualChannelLookupResult.Found("UC-no-uploads", "No Uploads", null, uploadsPlaylistId: null)
        };
        var repository = new FakeRepository();
        var scheduling = new FakeScheduling(repository);
        var useCase = Create(gateway, repository, scheduling);

        var result = await useCase.ManualApproveAsync(
            new ManualApproveChannelRequest("@nouploads"),
            CancellationToken.None);

        Assert.Equal(ChannelMutationStatus.ValidationError, result.Status);
        Assert.Equal(
            "This YouTube channel cannot be added because its uploads playlist is unavailable.",
            result.ValidationMessage);
        Assert.Equal(0, repository.CreateCount);
        Assert.Empty(scheduling.ScheduledChannelIds);
    }

    [Theory]
    [InlineData(ChannelStatus.APPROVED, false, "APPROVED")]
    [InlineData(ChannelStatus.REJECTED, false, "REJECTED")]
    [InlineData(ChannelStatus.APPROVED, true, ChannelUseCase.DeletedChannelStatus)]
    public async Task An_existing_channel_returns_its_matching_conflict_without_scheduling(
        ChannelStatus status,
        bool isDeleted,
        string expectedStatus)
    {
        var existing = new Channel
        {
            Id = Guid.NewGuid(),
            YoutubeChannelId = "UC-existing",
            Name = "Existing Channel",
            Status = status,
            BackfillStatus = BackfillStatus.PENDING,
            IsDeleted = isDeleted,
            DiscoveredAt = Now.AddDays(-1),
            CreatedAt = Now.AddDays(-1),
            ModifiedAt = Now.AddDays(-1)
        };
        var gateway = new FakeLookupGateway
        {
            Result = ManualChannelLookupResult.Found("UC-existing", "Existing Channel", null, "UU-existing")
        };
        var repository = new FakeRepository { ExistingByYoutubeChannelId = existing };
        var scheduling = new FakeScheduling(repository);
        var useCase = Create(gateway, repository, scheduling);

        var result = await useCase.ManualApproveAsync(
            new ManualApproveChannelRequest("@existing"),
            CancellationToken.None);

        Assert.Equal(ChannelMutationStatus.Conflict, result.Status);
        Assert.Equal(expectedStatus, result.CurrentStatus);
        Assert.Null(result.Channel);
        Assert.Null(result.ValidationMessage);
        Assert.Equal(1, repository.CreateCount);
        Assert.Null(repository.CreatedChannel);
        Assert.Empty(scheduling.ScheduledChannelIds);
    }

    [Fact]
    public async Task An_existing_pending_channel_uses_the_same_approval_and_scheduling_path()
    {
        var existing = new Channel
        {
            Id = Guid.NewGuid(),
            YoutubeChannelId = "UC-existing-pending",
            Name = "Existing Pending Channel",
            Status = ChannelStatus.PENDING,
            BackfillStatus = BackfillStatus.PENDING,
            IsDeleted = false,
            DiscoveredAt = Now.AddDays(-1),
            CreatedAt = Now.AddDays(-1),
            ModifiedAt = Now.AddDays(-1)
        };
        var gateway = new FakeLookupGateway
        {
            Result = ManualChannelLookupResult.Found(
                "UC-existing-pending",
                "Existing Pending Channel",
                null,
                "UU-existing-pending")
        };
        var repository = new FakeRepository { ExistingByYoutubeChannelId = existing };
        var scheduling = new FakeScheduling(repository);
        var useCase = Create(gateway, repository, scheduling);

        var result = await useCase.ManualApproveAsync(
            new ManualApproveChannelRequest("@existingpending"),
            CancellationToken.None);

        Assert.Equal(ChannelMutationStatus.Succeeded, result.Status);
        Assert.Equal(ChannelStatus.APPROVED.ToString(), result.Channel!.Status);
        Assert.Equal(1, repository.CreateCount);
        Assert.Equal(1, repository.ApprovalCount);
        Assert.Equal([existing.Id], scheduling.ScheduledChannelIds);
        Assert.Equal([BackfillTriggerSource.APPROVAL], scheduling.TriggerSources);
    }

    [Fact]
    public async Task A_scheduling_failure_is_returned_as_the_durable_scheduling_failed_state()
    {
        var gateway = new FakeLookupGateway
        {
            Result = ManualChannelLookupResult.Found("UC-flaky", "Flaky Channel", null, "UU-flaky")
        };
        var repository = new FakeRepository();
        var scheduling = new FakeScheduling(repository)
        {
            ResultingBackfillStatus = BackfillStatus.SCHEDULING_FAILED
        };
        var useCase = Create(gateway, repository, scheduling);

        var result = await useCase.ManualApproveAsync(
            new ManualApproveChannelRequest("@flaky"),
            CancellationToken.None);

        // The create already committed, so a scheduling failure is never a server error.
        Assert.Equal(ChannelMutationStatus.Succeeded, result.Status);
        Assert.NotNull(result.Channel);
        Assert.Equal(ChannelStatus.APPROVED.ToString(), result.Channel.Status);
        Assert.Equal(BackfillStatus.SCHEDULING_FAILED.ToString(), result.Channel.BackfillStatus);
        Assert.Equal(1, repository.CreateCount);
        Assert.Single(scheduling.ScheduledChannelIds);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task A_manual_add_is_refused_without_writing_when_a_required_first_page_bucket_is_known_exhausted(
        bool queriesMarker)
    {
        // The real ChannelRepository over InMemory is used deliberately: the marker check lives inside the one
        // paired reservation predicate that both the approval and manual-add flows share. The PostgreSQL
        // conditional-update predicate is proven by the PostgreSQL-only proof suite.
        await using var context = CreateFirstPageContext();
        AddLedgerWithMarker(context, queriesMarker);
        await context.SaveChangesAsync();

        var gateway = new FakeLookupGateway
        {
            Result = ManualChannelLookupResult.Found("UC-blocked", "Blocked", null, "UU-blocked")
        };
        var scheduling = new FakeScheduling(new FakeRepository());
        var useCase = new ChannelUseCase(
            new ChannelRepository(context),
            new NoopChannelActivityLogger(),
            scheduling,
            new FakeDailyQuotaRepository(),
            new FixedTimeProvider(Now),
            gateway,
            new FakeCurrentActor(ActorUserId));

        var result = await useCase.ManualApproveAsync(
            new ManualApproveChannelRequest("@blocked"),
            CancellationToken.None);

        Assert.Equal(ChannelMutationStatus.QuotaExceededToday, result.Status);
        Assert.Null(result.Channel);
        Assert.Empty(scheduling.ScheduledChannelIds);
        Assert.Equal(0, await context.Channels.CountAsync());

        var ledger = await context.YouTubeDailyQuotaUsages.AsNoTracking().SingleAsync();
        Assert.Equal(0, ledger.FirstPageAttemptUsed);
        Assert.Equal(0, ledger.QueriesUnitsUsed);
        Assert.Equal(0, ledger.VideoBatchStatsUnitsUsed);
    }

    private static YtFetcherDbContext CreateFirstPageContext() =>
        new(new DbContextOptionsBuilder<YtFetcherDbContext>()
            .UseInMemoryDatabase($"yt-fetcher-manual-marker-{Guid.NewGuid():N}")
            .ConfigureWarnings(warnings => warnings.Ignore(InMemoryEventId.TransactionIgnoredWarning))
            .Options);

    /// <summary>Seeds a ready first-page window that lacks a usable bucket because one marker is already set.</summary>
    private static void AddLedgerWithMarker(YtFetcherDbContext context, bool queriesMarker)
    {
        var ledger = new YouTubeDailyQuotaUsage
        {
            Id = Guid.NewGuid(),
            QuotaDate = PacificQuotaWindow.GetQuotaDate(Now),
            FirstPageAttemptLimit = 500,
            IsDeleted = false,
            CreatedAt = Now,
            ModifiedAt = Now
        };

        if (queriesMarker)
        {
            ledger.MarkQueriesQuotaExhausted(Now);
        }
        else
        {
            ledger.MarkVideoBatchStatsQuotaExhausted(Now);
        }

        context.YouTubeDailyQuotaUsages.Add(ledger);
    }

    private static ChannelUseCase Create(
        FakeLookupGateway gateway,
        FakeRepository repository,
        FakeScheduling scheduling) =>
        new(repository, new NoopChannelActivityLogger(), scheduling, new FakeDailyQuotaRepository(), new FixedTimeProvider(Now), gateway, new FakeCurrentActor(ActorUserId));

    private sealed class FakeCurrentActor(Guid userId) : ICurrentActor
    {
        public Guid UserId { get; } = userId;
    }

    private sealed class FixedTimeProvider(DateTimeOffset utcNow) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => utcNow;
    }

    private sealed class FakeLookupGateway : IYouTubeChannelLookupGateway
    {
        public ManualChannelLookupResult Result { get; init; } = ManualChannelLookupResult.NotFound();

        public List<string> ReceivedHandles { get; } = [];

        public Task<ManualChannelLookupResult> LookupByHandleAsync(
            string handle,
            CancellationToken cancellationToken)
        {
            ReceivedHandles.Add(handle);
            return Task.FromResult(Result);
        }
    }

    private sealed class FakeScheduling(FakeRepository repository) : IBackfillSchedulingUseCase
    {
        public List<Guid> ScheduledChannelIds { get; } = [];

        public List<BackfillTriggerSource> TriggerSources { get; } = [];

        public bool ChannelExistedWhenScheduled { get; private set; }

        public BackfillStatus ResultingBackfillStatus { get; init; } = BackfillStatus.PENDING;

        public Task ScheduleAsync(
            Guid channelId,
            BackfillTriggerSource triggerSource,
            CancellationToken cancellationToken)
        {
            ScheduledChannelIds.Add(channelId);
            TriggerSources.Add(triggerSource);
            ChannelExistedWhenScheduled = repository.StoredChannel is not null;

            if (repository.StoredChannel is { } channel)
            {
                channel.BackfillStatus = ResultingBackfillStatus;
            }

            return Task.CompletedTask;
        }
    }

    private sealed class NoopChannelActivityLogger : IChannelActivityLogger
    {
        public void LogApproved(Channel channel, ChannelStatus previousStatus)
        {
        }

        public void LogRejected(Channel channel, ChannelStatus previousStatus)
        {
        }

        public void LogReopened(Channel channel)
        {
        }

        public void LogSoftDeleted(Channel channel, ChannelStatus previousStatus)
        {
        }

        public void LogBackfillRetryRequested(Channel channel, BackfillStatus previousBackfillStatus)
        {
        }
    }

    /// <summary>
    /// Only the manual-add path is exercised: create-or-existing plus the active read used to return
    /// the durable row after scheduling. Every other member is intentionally unsupported.
    /// </summary>
    private sealed class FakeRepository : IChannelRepository
    {
        public Channel? ExistingByYoutubeChannelId { get; init; }

        public Channel? CreatedChannel { get; private set; }

        public Channel? StoredChannel => CreatedChannel ?? ExistingByYoutubeChannelId;

        public int CreateCount { get; private set; }

        public int ApprovalCount { get; private set; }

        public Task<ManualChannelCreateResult> CreateApprovedForSchedulingAsync(
            Channel channel,
            DateOnly quotaDate,
            CancellationToken cancellationToken)
        {
            CreateCount++;

            if (ExistingByYoutubeChannelId is not null)
            {
                return Task.FromResult(ManualChannelCreateResult.Duplicate(ExistingByYoutubeChannelId));
            }

            channel.FirstPageQuotaDate = quotaDate;
            CreatedChannel = channel;
            return Task.FromResult(ManualChannelCreateResult.Created(channel));
        }

        public Task<Channel?> GetActiveAsync(Guid channelId, CancellationToken cancellationToken) =>
            Task.FromResult(StoredChannel);

        public Task<Channel?> GetActiveForUpdateAsync(Guid channelId, CancellationToken cancellationToken) =>
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

        public Task<Channel?> TryApproveForSchedulingAsync(
            Guid channelId,
            DateTimeOffset utcNow,
            CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public Task<Channel?> TryCompleteSchedulingAsync(
            Guid channelId,
            DateTimeOffset utcNow,
            CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public Task<Channel?> TryFailSchedulingAsync(
            Guid channelId,
            string safeReason,
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
        public Task<ChannelApprovalResult> TryApproveForSchedulingAsync(
            Guid channelId,
            DateOnly quotaDate,
            Guid approvedByUserId,
            DateTimeOffset utcNow,
            CancellationToken cancellationToken)
        {
            ApprovalCount++;
            var channel = ExistingByYoutubeChannelId;
            if (channel is null || channel.Id != channelId ||
                !channel.TryBeginSchedulingAfterApproval(utcNow, approvedByUserId))
            {
                return Task.FromResult(ChannelApprovalResult.Refused(ChannelApprovalStatus.NotPending));
            }

            channel.FirstPageQuotaDate = quotaDate;
            return Task.FromResult(ChannelApprovalResult.Succeeded(channel));
        }

        public Task<ChannelBatchMutationResult> TryApproveManyForSchedulingAsync(IReadOnlyList<Guid> channelIds, DateOnly quotaDate, Guid approvedByUserId, DateTimeOffset utcNow, CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public Task<ChannelBatchMutationResult> TryRejectManyAsync(IReadOnlyList<Guid> channelIds, Guid rejectedByUserId, string reason, DateTimeOffset utcNow, CancellationToken cancellationToken) =>
            throw new NotSupportedException();
}

    private sealed class FakeDailyQuotaRepository : IDailyQuotaRepository
    {
        public Task<YouTubeDailyQuotaUsage?> GetForDateAsync(DateOnly quotaDate, CancellationToken cancellationToken) =>
            Task.FromResult<YouTubeDailyQuotaUsage?>(null);

        public Task EnsureLedgerAsync(DateOnly quotaDate, CancellationToken cancellationToken) => Task.CompletedTask;

        public Task<DailyProcessClaimDecision> TryClaimDailyProcessAsync(DateOnly quotaDate, Guid claimToken, DateTimeOffset utcNow, CancellationToken cancellationToken) =>
            Task.FromResult(DailyProcessClaimDecision.AlreadyRunning);

        public Task ReleaseDailyProcessClaimAsync(DateOnly quotaDate, Guid claimToken, CancellationToken cancellationToken) => Task.CompletedTask;

        public Task<DailyFinalizationResult> FinalizeDailyProcessingAsync(DateOnly quotaDate, Guid claimToken, DateTimeOffset utcNow, CancellationToken cancellationToken) =>
            Task.FromResult(new DailyFinalizationResult(false, false, 0, 0));

        public Task<QuotaReservationResult> TryReserveQueriesUnitAsync(DateOnly quotaDate, Guid? dailyProcessClaimToken, CancellationToken cancellationToken) =>
            Task.FromResult(QuotaReservationResult.Reserved);

        public Task<QuotaReservationResult> TryReserveVideoBatchStatsUnitAsync(DateOnly quotaDate, Guid? dailyProcessClaimToken, CancellationToken cancellationToken) =>
            Task.FromResult(QuotaReservationResult.Reserved);

        public Task<QuotaReservationResult> TryReserveSearchUnitAsync(DateOnly quotaDate, Guid? dailyProcessClaimToken, CancellationToken cancellationToken) =>
            Task.FromResult(QuotaReservationResult.Reserved);

        public Task MarkSearchQuotaExhaustedAsync(DateOnly quotaDate, DateTimeOffset utcNow, CancellationToken cancellationToken) => Task.CompletedTask;

        public Task MarkQueriesQuotaExhaustedAsync(DateOnly quotaDate, DateTimeOffset utcNow, CancellationToken cancellationToken) => Task.CompletedTask;

        public Task MarkVideoBatchStatsQuotaExhaustedAsync(DateOnly quotaDate, DateTimeOffset utcNow, CancellationToken cancellationToken) => Task.CompletedTask;
    }
}
