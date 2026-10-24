using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using YtFetcher.Application.Abstractions.Repositories;
using YtFetcher.Application.Abstractions.Services;
using YtFetcher.Application.Abstractions.UseCases;
using YtFetcher.Application.Backfill.UseCases;
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

public class ChannelUseCaseTests
{
    private const string RejectReason = "Irrelevant content for this niche";

    private static readonly DateTimeOffset Now = new(2026, 9, 22, 10, 0, 0, TimeSpan.Zero);
    private static readonly DateTimeOffset Earlier = Now.AddDays(-2);
    private static readonly Guid ActorUserId = Guid.NewGuid();

    [Fact]
    public async Task Approve_pending_channel_saves_and_logs_once()
    {
        var channel = CreatePendingChannel();
        var (useCase, repository, logger) = CreateUseCase(channel);

        var result = await useCase.ApproveAsync(channel.Id, new ApproveChannelRequest(), CancellationToken.None);

        Assert.Equal(ChannelMutationStatus.Succeeded, result.Status);
        Assert.NotNull(result.Channel);
        Assert.Equal(ChannelStatus.APPROVED.ToString(), result.Channel.Status);
        Assert.Equal(Now, result.Channel.ApprovedAt);
        Assert.Equal(BackfillStatus.PENDING.ToString(), result.Channel.BackfillStatus);
        Assert.Equal(1, repository.ApproveForSchedulingCount);
        Assert.Equal(ActorUserId, repository.ReceivedApprovalActorUserId);
        Assert.Equal(new[] { "Approved" }, logger.Events);
    }

    [Fact]
    public async Task Approve_missing_channel_returns_not_found_without_save_or_log()
    {
        var (useCase, repository, logger) = CreateUseCase(null);

        var result = await useCase.ApproveAsync(Guid.NewGuid(), new ApproveChannelRequest(), CancellationToken.None);

        Assert.Equal(ChannelMutationStatus.NotFound, result.Status);
        Assert.Null(result.Channel);
        Assert.Equal(0, repository.ApproveForSchedulingCount);
        Assert.Empty(logger.Events);
    }

    [Fact]
    public async Task Approve_non_pending_channel_returns_conflict_without_save_log_or_mutation()
    {
        var channel = CreatePendingChannel();
        channel.Status = ChannelStatus.APPROVED;
        channel.ApprovedAt = Earlier;
        var updatedAtBefore = channel.ModifiedAt;
        var (useCase, repository, logger) = CreateUseCase(channel);

        var result = await useCase.ApproveAsync(channel.Id, new ApproveChannelRequest(), CancellationToken.None);

        Assert.Equal(ChannelMutationStatus.Conflict, result.Status);
        Assert.Equal(ChannelStatus.APPROVED.ToString(), result.CurrentStatus);
        Assert.Equal(BackfillStatus.PENDING.ToString(), result.CurrentBackfillStatus);
        Assert.Equal(0, repository.ApproveForSchedulingCount);
        Assert.Empty(logger.Events);
        Assert.Equal(Earlier, channel.ApprovedAt);
        Assert.Equal(updatedAtBefore, channel.ModifiedAt);
    }

    [Fact]
    public async Task Reject_pending_channel_saves_the_trimmed_reason_and_logs_once()
    {
        var channel = CreatePendingChannel();
        var (useCase, repository, logger) = CreateUseCase(channel);

        var result = await useCase.RejectAsync(
            channel.Id,
            new RejectChannelRequest($"  {RejectReason}  "),
            CancellationToken.None);

        Assert.Equal(ChannelMutationStatus.Succeeded, result.Status);
        Assert.NotNull(result.Channel);
        Assert.Equal(ChannelStatus.REJECTED.ToString(), result.Channel.Status);
        Assert.Equal(Now, result.Channel.RejectedAt);
        Assert.Equal(ActorUserId, channel.RejectedByUserId);
        Assert.Equal(ActorUserId, channel.ModifiedByUserId);
        Assert.Equal(RejectReason, channel.RejectionReason);
        Assert.Equal(1, repository.SaveCount);
        Assert.Equal(new[] { "Rejected" }, logger.Events);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public async Task Reject_blank_reason_returns_a_validation_response_without_mutating(string? reason)
    {
        var channel = CreatePendingChannel();
        var (useCase, repository, logger) = CreateUseCase(channel);

        var result = await useCase.RejectAsync(
            channel.Id,
            new RejectChannelRequest(reason),
            CancellationToken.None);

        Assert.Equal(ChannelMutationStatus.ValidationError, result.Status);
        Assert.Equal("A rejection reason is required.", result.ValidationMessage);
        Assert.Equal(ChannelStatus.PENDING, channel.Status);
        Assert.Equal(0, repository.SaveCount);
        Assert.Empty(logger.Events);
    }

    [Fact]
    public async Task Reject_overlong_reason_returns_a_validation_response_without_mutating()
    {
        var channel = CreatePendingChannel();
        var (useCase, repository, logger) = CreateUseCase(channel);
        var reason = new string('a', Channel.RejectionReasonMaxLength + 1);

        var result = await useCase.RejectAsync(
            channel.Id,
            new RejectChannelRequest(reason),
            CancellationToken.None);

        Assert.Equal(ChannelMutationStatus.ValidationError, result.Status);
        Assert.Equal(
            $"A rejection reason cannot exceed {Channel.RejectionReasonMaxLength} characters.",
            result.ValidationMessage);
        Assert.Equal(ChannelStatus.PENDING, channel.Status);
        Assert.Equal(0, repository.SaveCount);
        Assert.Empty(logger.Events);
    }

    [Fact]
    public async Task Reject_missing_channel_returns_not_found_without_save_or_log()
    {
        var (useCase, repository, logger) = CreateUseCase(null);

        var result = await useCase.RejectAsync(
            Guid.NewGuid(),
            new RejectChannelRequest(RejectReason),
            CancellationToken.None);

        Assert.Equal(ChannelMutationStatus.NotFound, result.Status);
        Assert.Equal(0, repository.SaveCount);
        Assert.Empty(logger.Events);
    }

    [Fact]
    public async Task Reject_rejected_channel_returns_conflict_without_save_log_or_mutation()
    {
        var channel = CreatePendingChannel();
        channel.Status = ChannelStatus.REJECTED;
        channel.RejectedAt = Earlier;
        channel.RejectionReason = "Earlier decision";
        var updatedAtBefore = channel.ModifiedAt;
        var (useCase, repository, logger) = CreateUseCase(channel);

        var result = await useCase.RejectAsync(
            channel.Id,
            new RejectChannelRequest(RejectReason),
            CancellationToken.None);

        Assert.Equal(ChannelMutationStatus.Conflict, result.Status);
        Assert.Equal(ChannelStatus.REJECTED.ToString(), result.CurrentStatus);
        Assert.Equal(0, repository.SaveCount);
        Assert.Empty(logger.Events);
        Assert.Equal(Earlier, channel.RejectedAt);
        Assert.Equal("Earlier decision", channel.RejectionReason);
        Assert.Equal(updatedAtBefore, channel.ModifiedAt);
    }

    [Fact]
    public async Task Soft_delete_active_channel_saves_and_logs_once()
    {
        var channel = CreatePendingChannel();
        var (useCase, repository, logger) = CreateUseCase(channel);

        var result = await useCase.SoftDeleteAsync(channel.Id, new DeleteChannelRequest(), CancellationToken.None);

        Assert.Equal(ChannelMutationStatus.Succeeded, result.Status);
        Assert.Null(result.Channel);
        Assert.True(channel.IsDeleted);
        Assert.Equal(ActorUserId, channel.DeletedByUserId);
        Assert.Equal(ActorUserId, channel.ModifiedByUserId);
        Assert.Equal(Now, channel.DeletedAt);
        Assert.Equal(1, repository.SaveCount);
        Assert.Equal(new[] { "SoftDeleted" }, logger.Events);
    }

    [Fact]
    public async Task Soft_delete_missing_channel_returns_not_found_without_save_or_log()
    {
        var (useCase, repository, logger) = CreateUseCase(null);

        var result = await useCase.SoftDeleteAsync(Guid.NewGuid(), new DeleteChannelRequest(), CancellationToken.None);

        Assert.Equal(ChannelMutationStatus.NotFound, result.Status);
        Assert.Equal(0, repository.SaveCount);
        Assert.Empty(logger.Events);
    }

    [Fact]
    public async Task Soft_delete_already_deleted_channel_returns_conflict_without_save_log_or_mutation()
    {
        var channel = CreatePendingChannel();
        channel.SoftDelete(Earlier, null);
        var deletedAtBefore = channel.DeletedAt;
        var updatedAtBefore = channel.ModifiedAt;
        var (useCase, repository, logger) = CreateUseCase(channel);

        var result = await useCase.SoftDeleteAsync(channel.Id, new DeleteChannelRequest(), CancellationToken.None);

        Assert.Equal(ChannelMutationStatus.Conflict, result.Status);
        Assert.Equal(0, repository.SaveCount);
        Assert.Empty(logger.Events);
        Assert.Equal(deletedAtBefore, channel.DeletedAt);
        Assert.Equal(updatedAtBefore, channel.ModifiedAt);
    }

    [Fact]
    public async Task Get_by_id_maps_the_active_channel_to_detail()
    {
        var channel = CreatePendingChannel();
        var (useCase, _, _) = CreateUseCase(channel);

        var detail = await useCase.GetByIdAsync(channel.Id, CancellationToken.None);

        Assert.NotNull(detail);
        Assert.Equal(channel.Id, detail.Id);
        Assert.Equal(channel.YoutubeChannelId, detail.YoutubeChannelId);
        Assert.Equal(ChannelStatus.PENDING.ToString(), detail.Status);
        Assert.Equal(channel.ThumbnailUrl, detail.ThumbnailUrl);
    }

    [Fact]
    public async Task Get_by_id_returns_null_when_the_repository_has_no_active_channel()
    {
        var (useCase, _, _) = CreateUseCase(null);

        var detail = await useCase.GetByIdAsync(Guid.NewGuid(), CancellationToken.None);

        Assert.Null(detail);
    }

    [Fact]
    public async Task Get_pending_maps_repository_entities_and_counts_to_the_response()
    {
        var channel = CreatePendingChannel();
        var repository = new FakeChannelRepository
        {
            PendingChannels = [channel],
            PendingCount = 7,
            EvidenceCounts = new Dictionary<Guid, int> { [channel.Id] = 3 }
        };
        var useCase = new ChannelUseCase(repository, new FakeChannelActivityLogger(), new FakeBackfillSchedulingUseCase(repository), new FakeDailyQuotaRepository(), new FixedTimeProvider(Now), new FakeYouTubeChannelLookupGateway(), new FakeCurrentActor(ActorUserId));

        var result = await useCase.GetPendingAsync(
            new PendingChannelsRequest(Page: 2, PageSize: 5, SearchText: null),
            CancellationToken.None);

        Assert.Equal(2, result.Page);
        Assert.Equal(5, result.PageSize);
        Assert.Equal(7, result.TotalCount);
        var item = Assert.Single(result.Items);
        Assert.Equal(channel.Id, item.Id);
        Assert.Equal(channel.YoutubeChannelId, item.YoutubeChannelId);
        Assert.Equal(channel.Name, item.Name);
        Assert.Equal(channel.ThumbnailUrl, item.ThumbnailUrl);
        Assert.Equal(channel.DiscoveredAt, item.DiscoveredAt);
        Assert.Equal(3, item.EvidenceCount);
        Assert.Equal(2, repository.ReceivedPage);
        Assert.Equal(5, repository.ReceivedPageSize);
    }

    [Fact]
    public async Task Get_pending_forwards_the_normalized_search_text_to_the_repository()
    {
        var repository = new FakeChannelRepository();
        var useCase = new ChannelUseCase(repository, new FakeChannelActivityLogger(), new FakeBackfillSchedulingUseCase(repository), new FakeDailyQuotaRepository(), new FixedTimeProvider(Now), new FakeYouTubeChannelLookupGateway(), new FakeCurrentActor(ActorUserId));

        await useCase.GetPendingAsync(
            new PendingChannelsRequest(Page: 1, PageSize: 10, SearchText: "  Kyoto  "),
            CancellationToken.None);

        Assert.Equal("Kyoto", repository.ReceivedSearchText);
    }

    [Fact]
    public async Task Get_evidence_maps_entities_and_resolved_keywords_to_the_response()
    {
        var channel = CreatePendingChannel();
        var keywordId = Guid.NewGuid();
        var repository = new FakeChannelRepository
        {
            ActiveChannel = channel,
            EvidenceEntities = [Evidence(channel, keywordId, "video-1", Earlier)],
            Keywords = new Dictionary<Guid, DiscoveryKeyword> { [keywordId] = Keyword(keywordId, "sakura city") }
        };
        var useCase = new ChannelUseCase(repository, new FakeChannelActivityLogger(), new FakeBackfillSchedulingUseCase(repository), new FakeDailyQuotaRepository(), new FixedTimeProvider(Now), new FakeYouTubeChannelLookupGateway(), new FakeCurrentActor(ActorUserId));

        var result = await useCase.GetEvidenceAsync(channel.Id, CancellationToken.None);

        Assert.NotNull(result);
        var item = Assert.Single(result.Items);
        Assert.Equal("video-1", item.YoutubeVideoId);
        Assert.Equal("Evidence video video-1", item.Title);
        Assert.Equal("https://example.com/video-1.jpg", item.ThumbnailUrl);
        Assert.Equal(Earlier, item.PublishedAt);
        Assert.Equal("sakura city", item.Keyword);
        Assert.Equal(5, repository.ReceivedLimit);
    }

    [Fact]
    public async Task Get_evidence_looks_up_every_distinct_keyword_id_with_one_repository_call()
    {
        var channel = CreatePendingChannel();
        var firstKeywordId = Guid.NewGuid();
        var secondKeywordId = Guid.NewGuid();
        var repository = new FakeChannelRepository
        {
            ActiveChannel = channel,
            EvidenceEntities =
            [
                Evidence(channel, firstKeywordId, "video-1", Earlier),
                Evidence(channel, firstKeywordId, "video-2", Earlier.AddDays(-1)),
                Evidence(channel, secondKeywordId, "video-3", Earlier.AddDays(-2))
            ],
            Keywords = new Dictionary<Guid, DiscoveryKeyword>
            {
                [firstKeywordId] = Keyword(firstKeywordId, "sakura city"),
                [secondKeywordId] = Keyword(secondKeywordId, "kota sakura")
            }
        };
        var useCase = new ChannelUseCase(repository, new FakeChannelActivityLogger(), new FakeBackfillSchedulingUseCase(repository), new FakeDailyQuotaRepository(), new FixedTimeProvider(Now), new FakeYouTubeChannelLookupGateway(), new FakeCurrentActor(ActorUserId));

        var result = await useCase.GetEvidenceAsync(channel.Id, CancellationToken.None);

        Assert.NotNull(result);
        Assert.Equal(3, result.Items.Count);
        Assert.Equal(1, repository.KeywordLookupCount);
        Assert.Equal(2, repository.ReceivedKeywordIds.Count);
        Assert.Contains(firstKeywordId, repository.ReceivedKeywordIds);
        Assert.Contains(secondKeywordId, repository.ReceivedKeywordIds);
    }

    [Fact]
    public async Task Get_evidence_maps_a_missing_keyword_to_the_fallback_without_throwing()
    {
        var channel = CreatePendingChannel();
        var repository = new FakeChannelRepository
        {
            ActiveChannel = channel,
            EvidenceEntities = [Evidence(channel, Guid.NewGuid(), "video-1", Earlier)],
            Keywords = new Dictionary<Guid, DiscoveryKeyword>()
        };
        var useCase = new ChannelUseCase(repository, new FakeChannelActivityLogger(), new FakeBackfillSchedulingUseCase(repository), new FakeDailyQuotaRepository(), new FixedTimeProvider(Now), new FakeYouTubeChannelLookupGateway(), new FakeCurrentActor(ActorUserId));

        var result = await useCase.GetEvidenceAsync(channel.Id, CancellationToken.None);

        Assert.NotNull(result);
        var item = Assert.Single(result.Items);
        Assert.Equal(ChannelEvidenceDto.UnknownKeyword, item.Keyword);
    }

    [Fact]
    public async Task Get_evidence_returns_an_empty_response_without_a_keyword_lookup_when_there_is_no_evidence()
    {
        var channel = CreatePendingChannel();
        var repository = new FakeChannelRepository { ActiveChannel = channel };
        var useCase = new ChannelUseCase(repository, new FakeChannelActivityLogger(), new FakeBackfillSchedulingUseCase(repository), new FakeDailyQuotaRepository(), new FixedTimeProvider(Now), new FakeYouTubeChannelLookupGateway(), new FakeCurrentActor(ActorUserId));

        var result = await useCase.GetEvidenceAsync(channel.Id, CancellationToken.None);

        Assert.NotNull(result);
        Assert.Empty(result.Items);
        Assert.Equal(0, repository.KeywordLookupCount);
    }

    [Fact]
    public async Task Get_evidence_returns_null_without_querying_when_the_channel_is_missing_or_soft_deleted()
    {
        var (useCase, repository, _) = CreateUseCase(null);

        var result = await useCase.GetEvidenceAsync(Guid.NewGuid(), CancellationToken.None);

        Assert.Null(result);
        Assert.Null(repository.ReceivedLimit);
        Assert.Equal(0, repository.KeywordLookupCount);
    }

    [Fact]
    public async Task Reject_approved_channel_moves_it_to_rejected_and_preserves_backfill_state()
    {
        var channel = CreatePendingChannel();
        channel.Status = ChannelStatus.APPROVED;
        channel.ApprovedAt = Earlier;
        channel.BackfillStatus = BackfillStatus.RUNNING;
        channel.BackfillPageToken = "next-page";
        var claimToken = Guid.NewGuid();
        channel.BackfillClaimToken = claimToken;
        channel.BackfillClaimedAt = Earlier;
        channel.BackfillCutoffAt = Earlier.AddDays(-365);
        var (useCase, repository, logger) = CreateUseCase(channel);

        var result = await useCase.RejectAsync(
            channel.Id,
            new RejectChannelRequest("  Not a fit  "),
            CancellationToken.None);

        Assert.Equal(ChannelMutationStatus.Succeeded, result.Status);
        Assert.NotNull(result.Channel);
        Assert.Equal(ChannelStatus.REJECTED.ToString(), result.Channel.Status);
        Assert.Equal(Now, result.Channel.RejectedAt);
        Assert.Equal("Not a fit", channel.RejectionReason);
        Assert.Equal(1, repository.SaveCount);
        Assert.Equal(new[] { "Rejected" }, logger.Events);

        // The existing backfill claim/progress is intentionally preserved.
        Assert.Equal(BackfillStatus.RUNNING, channel.BackfillStatus);
        Assert.Equal("next-page", channel.BackfillPageToken);
        Assert.Equal(claimToken, channel.BackfillClaimToken);
        Assert.Equal(Earlier, channel.BackfillClaimedAt);
        Assert.Equal(Earlier.AddDays(-365), channel.BackfillCutoffAt);
    }

    [Fact]
    public async Task Reopen_assigns_the_current_actor_to_the_modified_audit_field()
    {
        var channel = CreatePendingChannel();
        channel.Status = ChannelStatus.REJECTED;
        channel.RejectedAt = Earlier;
        channel.RejectedByUserId = Guid.NewGuid();
        channel.RejectionReason = RejectReason;
        var (useCase, repository, _) = CreateUseCase(channel);

        var result = await useCase.ReopenAsync(
            channel.Id,
            new ReopenChannelRequest(),
            CancellationToken.None);

        Assert.Equal(ChannelMutationStatus.Succeeded, result.Status);
        Assert.Equal(ChannelStatus.PENDING.ToString(), result.Channel!.Status);
        Assert.Null(channel.RejectedAt);
        Assert.Null(channel.RejectedByUserId);
        Assert.Null(channel.RejectionReason);
        Assert.Equal(ActorUserId, channel.ModifiedByUserId);
        Assert.Equal(1, repository.SaveCount);
    }

    private static (ChannelUseCase UseCase, FakeChannelRepository Repository, FakeChannelActivityLogger Logger)
        CreateUseCase(Channel? channel)
    {
        var repository = new FakeChannelRepository
        {
            ActiveChannel = channel,
            UpdateChannel = channel
        };
        var logger = new FakeChannelActivityLogger();
        var useCase = new ChannelUseCase(
            repository,
            logger,
            new FakeBackfillSchedulingUseCase(repository),
            new FakeDailyQuotaRepository(), new FixedTimeProvider(Now),
            new FakeYouTubeChannelLookupGateway(), new FakeCurrentActor(ActorUserId));

        return (useCase, repository, logger);
    }

    private static Channel CreatePendingChannel() => new()
    {
        Id = Guid.NewGuid(),
        YoutubeChannelId = "UC-use-case",
        Name = "Use case channel",
        ThumbnailUrl = "https://example.com/use-case.jpg",
        Status = ChannelStatus.PENDING,
        UploadsPlaylistId = "PL-use-case",
        BackfillStatus = BackfillStatus.PENDING,
        IsDeleted = false,
        DiscoveredAt = Earlier,
        CreatedAt = Earlier,
        ModifiedAt = Earlier
    };

    private sealed class FakeCurrentActor(Guid userId) : ICurrentActor
    {
        public Guid UserId { get; } = userId;
    }

    [Fact]
    public async Task Get_approved_channels_maps_entities_and_forwards_the_normalized_search_text()
    {
        var approved = CreatePendingChannel();
        approved.Status = ChannelStatus.APPROVED;
        approved.ApprovedAt = Now;
        var repository = new FakeChannelRepository { ReviewChannels = [approved], ReviewCount = 42 };
        var useCase = new ChannelUseCase(repository, new FakeChannelActivityLogger(), new FakeBackfillSchedulingUseCase(repository), new FakeDailyQuotaRepository(), new FixedTimeProvider(Now), new FakeYouTubeChannelLookupGateway(), new FakeCurrentActor(ActorUserId));

        var result = await useCase.GetApprovedChannelsAsync(
            new ApprovedChannelsRequest(Page: 2, PageSize: 10, SearchText: "  sakura  "),
            CancellationToken.None);

        Assert.Equal(2, result.Page);
        Assert.Equal(10, result.PageSize);
        Assert.Equal(42, result.TotalCount);
        var item = Assert.Single(result.Items);
        Assert.Equal(approved.Id, item.Id);
        Assert.Equal(approved.YoutubeChannelId, item.YoutubeChannelId);
        Assert.Equal(approved.Name, item.Name);
        Assert.Equal(Now, item.ApprovedAt);
        Assert.Equal("PENDING", item.BackfillStatus);
        Assert.Null(item.LastUploadCheckAt);
        Assert.Equal(ChannelStatus.APPROVED, repository.ReceivedReviewStatus);
        Assert.Equal("sakura", repository.ReceivedSearchText);
    }

    [Fact]
    public async Task Get_approved_channels_treats_a_blank_search_text_as_no_filter()
    {
        var repository = new FakeChannelRepository();
        var useCase = new ChannelUseCase(repository, new FakeChannelActivityLogger(), new FakeBackfillSchedulingUseCase(repository), new FakeDailyQuotaRepository(), new FixedTimeProvider(Now), new FakeYouTubeChannelLookupGateway(), new FakeCurrentActor(ActorUserId));

        await useCase.GetApprovedChannelsAsync(
            new ApprovedChannelsRequest(Page: 1, PageSize: 10, SearchText: "   "),
            CancellationToken.None);

        Assert.Null(repository.ReceivedSearchText);
    }

    [Fact]
    public async Task Get_rejected_channels_maps_the_persisted_rejection_reason()
    {
        var rejected = CreatePendingChannel();
        rejected.Status = ChannelStatus.REJECTED;
        rejected.RejectedAt = Now;
        rejected.RejectionReason = "Irrelevant content for this niche";
        var repository = new FakeChannelRepository { ReviewChannels = [rejected], ReviewCount = 7 };
        var useCase = new ChannelUseCase(repository, new FakeChannelActivityLogger(), new FakeBackfillSchedulingUseCase(repository), new FakeDailyQuotaRepository(), new FixedTimeProvider(Now), new FakeYouTubeChannelLookupGateway(), new FakeCurrentActor(ActorUserId));

        var result = await useCase.GetRejectedChannelsAsync(
            new RejectedChannelsRequest(Page: 1, PageSize: 5, SearchText: "uc-"),
            CancellationToken.None);

        Assert.Equal(7, result.TotalCount);
        var item = Assert.Single(result.Items);
        Assert.Equal(Now, item.RejectedAt);
        Assert.Equal("Irrelevant content for this niche", item.RejectionReason);
        Assert.Equal(ChannelStatus.REJECTED, repository.ReceivedReviewStatus);
    }

    [Fact]
    public async Task Get_review_counts_covers_every_status_and_defaults_missing_ones_to_zero()
    {
        var repository = new FakeChannelRepository
        {
            StatusCounts = new Dictionary<ChannelStatus, int>
            {
                [ChannelStatus.PENDING] = 175,
                [ChannelStatus.APPROVED] = 12
            }
        };
        var useCase = new ChannelUseCase(repository, new FakeChannelActivityLogger(), new FakeBackfillSchedulingUseCase(repository), new FakeDailyQuotaRepository(), new FixedTimeProvider(Now), new FakeYouTubeChannelLookupGateway(), new FakeCurrentActor(ActorUserId));

        var counts = await useCase.GetReviewCountsAsync(CancellationToken.None);

        Assert.Equal(175, counts.Pending);
        Assert.Equal(12, counts.Approved);
        Assert.Equal(0, counts.Rejected);
    }

    [Fact]
    public async Task Approve_commits_scheduling_before_enqueue_and_returns_pending_after_it()
    {
        var channel = CreatePendingChannel();
        var repository = new FakeChannelRepository { ActiveChannel = channel, UpdateChannel = channel };
        var scheduling = new FakeBackfillSchedulingUseCase(repository);
        var useCase = new ChannelUseCase(
            repository,
            new FakeChannelActivityLogger(),
            scheduling,
            new FakeDailyQuotaRepository(), new FixedTimeProvider(Now),
            new FakeYouTubeChannelLookupGateway(), new FakeCurrentActor(ActorUserId));

        var result = await useCase.ApproveAsync(channel.Id, new ApproveChannelRequest(), CancellationToken.None);

        Assert.Equal(ChannelMutationStatus.Succeeded, result.Status);
        Assert.NotNull(result.Channel);
        Assert.Equal(ChannelStatus.APPROVED.ToString(), result.Channel.Status);
        Assert.Equal(BackfillStatus.PENDING.ToString(), result.Channel.BackfillStatus);
        Assert.Equal(1, repository.ApproveForSchedulingCount);
        Assert.Equal(new[] { channel.Id }, scheduling.ScheduledChannelIds);
        Assert.Equal([BackfillTriggerSource.APPROVAL], scheduling.TriggerSources);

        // The committed approval was already SCHEDULING when the enqueue ran, so no job can observe a
        // channel that is still pending.
        Assert.Equal(BackfillStatus.SCHEDULING, scheduling.StatusWhenScheduled);
    }

    [Fact]
    public async Task Approve_that_cannot_be_scheduled_returns_scheduling_failed_without_rolling_back()
    {
        var channel = CreatePendingChannel();
        var repository = new FakeChannelRepository { ActiveChannel = channel, UpdateChannel = channel };
        var scheduling = new FakeBackfillSchedulingUseCase(repository)
        {
            ResultingBackfillStatus = BackfillStatus.SCHEDULING_FAILED
        };
        var useCase = new ChannelUseCase(
            repository,
            new FakeChannelActivityLogger(),
            scheduling,
            new FakeDailyQuotaRepository(), new FixedTimeProvider(Now),
            new FakeYouTubeChannelLookupGateway(), new FakeCurrentActor(ActorUserId));

        var result = await useCase.ApproveAsync(channel.Id, new ApproveChannelRequest(), CancellationToken.None);

        Assert.Equal(ChannelMutationStatus.Succeeded, result.Status);
        Assert.NotNull(result.Channel);
        Assert.Equal(ChannelStatus.APPROVED.ToString(), result.Channel.Status);
        Assert.Equal(BackfillStatus.SCHEDULING_FAILED.ToString(), result.Channel.BackfillStatus);
        Assert.Equal(BackfillSchedulingUseCase.SafeSchedulingFailureReason, result.Channel.BackfillSchedulingFailureReason);

        // A scheduling failure never counts as a backfill execution failure.
        Assert.Equal(0, channel.BackfillFailureCount);
    }

    [Fact]
    public async Task Approve_that_is_not_pending_neither_schedules_nor_mutates()
    {
        var channel = CreatePendingChannel();
        channel.Status = ChannelStatus.APPROVED;
        var repository = new FakeChannelRepository { ActiveChannel = channel, UpdateChannel = channel };
        var scheduling = new FakeBackfillSchedulingUseCase(repository);
        var useCase = new ChannelUseCase(
            repository,
            new FakeChannelActivityLogger(),
            scheduling,
            new FakeDailyQuotaRepository(), new FixedTimeProvider(Now),
            new FakeYouTubeChannelLookupGateway(), new FakeCurrentActor(ActorUserId));

        var result = await useCase.ApproveAsync(channel.Id, new ApproveChannelRequest(), CancellationToken.None);

        Assert.Equal(ChannelMutationStatus.Conflict, result.Status);
        Assert.Empty(scheduling.ScheduledChannelIds);
        Assert.Equal(0, repository.ApproveForSchedulingCount);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task Approve_is_refused_without_mutation_when_a_required_first_page_bucket_is_known_exhausted(
        bool queriesMarker)
    {
        // The real ChannelRepository over InMemory is used deliberately: the marker check lives inside the paired
        // reservation predicate, so a fake repository would not exercise it. The PostgreSQL conditional-update
        // predicate is proven by the PostgreSQL-only proof suite.
        await using var context = CreateFirstPageContext();
        var channel = AddPendingChannelForMarkerTest(context);
        AddLedgerWithMarker(context, queriesMarker);
        await context.SaveChangesAsync();

        var scheduling = new FakeBackfillSchedulingUseCase(new FakeChannelRepository());
        var useCase = new ChannelUseCase(
            new ChannelRepository(context),
            new FakeChannelActivityLogger(),
            scheduling,
            new FakeDailyQuotaRepository(),
            new FixedTimeProvider(Now),
            new FakeYouTubeChannelLookupGateway(), new FakeCurrentActor(ActorUserId));

        var result = await useCase.ApproveAsync(channel.Id, new ApproveChannelRequest(), CancellationToken.None);

        Assert.Equal(ChannelMutationStatus.QuotaExceededToday, result.Status);
        Assert.Null(result.Channel);
        Assert.Empty(scheduling.ScheduledChannelIds);

        await AssertFirstPageReservationUntouchedAsync(context, channel.Id);
    }

    private static YtFetcherDbContext CreateFirstPageContext() =>
        new(new DbContextOptionsBuilder<YtFetcherDbContext>()
            .UseInMemoryDatabase($"yt-fetcher-approval-marker-{Guid.NewGuid():N}")
            .ConfigureWarnings(warnings => warnings.Ignore(InMemoryEventId.TransactionIgnoredWarning))
            .Options);

    private static Channel AddPendingChannelForMarkerTest(YtFetcherDbContext context)
    {
        var channel = new Channel
        {
            Id = Guid.NewGuid(),
            YoutubeChannelId = $"UC-{Guid.NewGuid():N}",
            Name = "Marker approval channel",
            Status = ChannelStatus.PENDING,
            BackfillStatus = BackfillStatus.PENDING,
            IsDeleted = false,
            DiscoveredAt = Earlier,
            CreatedAt = Earlier,
            ModifiedAt = Earlier
        };

        context.Channels.Add(channel);
        return channel;
    }

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

    private static async Task AssertFirstPageReservationUntouchedAsync(YtFetcherDbContext context, Guid channelId)
    {
        var ledger = await context.YouTubeDailyQuotaUsages.AsNoTracking().SingleAsync();
        Assert.Equal(0, ledger.FirstPageAttemptUsed);
        Assert.Equal(0, ledger.QueriesUnitsUsed);
        Assert.Equal(0, ledger.VideoBatchStatsUnitsUsed);

        var stored = await context.Channels.AsNoTracking().SingleAsync(entity => entity.Id == channelId);
        Assert.Equal(ChannelStatus.PENDING, stored.Status);
        Assert.Equal(BackfillStatus.PENDING, stored.BackfillStatus);
        Assert.Null(stored.FirstPageQuotaDate);
    }

    private static DiscoveryKeyword Keyword(Guid id, string value) => new()
    {
        Id = id,
        Keyword = value,
        IsActive = true,
        IsDeleted = false,
        CreatedAt = Earlier,
        ModifiedAt = Earlier
    };

    private static ChannelDiscovery Evidence(
        Channel channel,
        Guid keywordId,
        string youtubeVideoId,
        DateTimeOffset publishedAt) => new()
    {
        Id = Guid.NewGuid(),
        ChannelId = channel.Id,
        KeywordId = keywordId,
        YoutubeVideoId = youtubeVideoId,
        VideoTitle = $"Evidence video {youtubeVideoId}",
        VideoThumbnailUrl = $"https://example.com/{youtubeVideoId}.jpg",
        VideoPublishedAt = publishedAt,
        DiscoveredAt = Earlier,
        IsDeleted = false,
        CreatedAt = Earlier,
        ModifiedAt = Earlier
    };

    private sealed class FakeChannelRepository : IChannelRepository
    {
        public Channel? ActiveChannel { get; init; }

        public Channel? UpdateChannel { get; init; }

        public IReadOnlyList<Channel> ReviewChannels { get; init; } = [];

        public int ReviewCount { get; init; }

        public IReadOnlyDictionary<ChannelStatus, int> StatusCounts { get; init; } =
            new Dictionary<ChannelStatus, int>();

        public ChannelStatus? ReceivedReviewStatus { get; private set; }

        public IReadOnlyList<Channel> PendingChannels { get; init; } = [];

        public int PendingCount { get; init; }

        public IReadOnlyDictionary<Guid, int> EvidenceCounts { get; init; } = new Dictionary<Guid, int>();

        public IReadOnlyList<ChannelDiscovery> EvidenceEntities { get; init; } = [];

        public IReadOnlyDictionary<Guid, DiscoveryKeyword> Keywords { get; init; } =
            new Dictionary<Guid, DiscoveryKeyword>();

        public int KeywordLookupCount { get; private set; }

        public IReadOnlyCollection<Guid> ReceivedKeywordIds { get; private set; } = [];

        public string? ReceivedSearchText { get; private set; }

        public int? ReceivedPage { get; private set; }

        public int? ReceivedPageSize { get; private set; }

        public int? ReceivedLimit { get; private set; }

        public int SaveCount { get; private set; }

        public Task<Channel?> GetActiveAsync(Guid channelId, CancellationToken cancellationToken) =>
            Task.FromResult(ActiveChannel);

        public Task<Channel?> GetActiveForUpdateAsync(Guid channelId, CancellationToken cancellationToken) =>
            Task.FromResult(UpdateChannel);

        public Task<ManualChannelCreateResult> CreateApprovedForSchedulingAsync(
            Channel channel,
            CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public Task<IReadOnlyList<Channel>> GetPendingPageAsync(
            int page,
            int pageSize,
            string? searchText,
            CancellationToken cancellationToken)
        {
            ReceivedPage = page;
            ReceivedPageSize = pageSize;
            ReceivedSearchText = searchText;
            return Task.FromResult(PendingChannels);
        }

        public Task<int> CountPendingAsync(string? searchText, CancellationToken cancellationToken) =>
            Task.FromResult(PendingCount);

        public Task<IReadOnlyDictionary<Guid, int>> CountEvidenceAsync(
            IReadOnlyCollection<Guid> channelIds,
            CancellationToken cancellationToken) =>
            Task.FromResult(EvidenceCounts);

        public Task<IReadOnlyList<Channel>> GetReviewPageAsync(
            ChannelStatus status,
            int page,
            int pageSize,
            string? searchText,
            CancellationToken cancellationToken)
        {
            ReceivedReviewStatus = status;
            ReceivedSearchText = searchText;
            return Task.FromResult(ReviewChannels);
        }

        public Task<int> CountReviewChannelsAsync(
            ChannelStatus status,
            string? searchText,
            CancellationToken cancellationToken)
        {
            ReceivedReviewStatus = status;
            return Task.FromResult(ReviewCount);
        }

        public Task<IReadOnlyDictionary<ChannelStatus, int>> CountChannelsByStatusAsync(
            CancellationToken cancellationToken) =>
            Task.FromResult(StatusCounts);

        public Task<IReadOnlyList<ChannelDiscovery>> GetEvidenceAsync(
            Guid channelId,
            int limit,
            CancellationToken cancellationToken)
        {
            ReceivedLimit = limit;
            return Task.FromResult(EvidenceEntities);
        }

        public Task<IReadOnlyDictionary<Guid, DiscoveryKeyword>> GetDiscoveryKeywordsByIdsAsync(
            IReadOnlyCollection<Guid> keywordIds,
            CancellationToken cancellationToken)
        {
            KeywordLookupCount++;
            ReceivedKeywordIds = keywordIds;
            return Task.FromResult(Keywords);
        }

        public Task SaveChangesAsync(CancellationToken cancellationToken)
        {
            SaveCount++;
            return Task.CompletedTask;
        }

        public int ApproveForSchedulingCount { get; private set; }

        public Guid? ReceivedApprovalActorUserId { get; private set; }

        public int FailSchedulingCount { get; private set; }

        public int LeaseStaleSchedulingCount { get; private set; }

        public IReadOnlyList<Guid> StaleSchedulingChannelIds { get; init; } = [];

        public DateTimeOffset? ReceivedStaleBefore { get; private set; }

        public int? ReceivedMaximumLeaseCount { get; private set; }

        public Task<Channel?> TryApproveForSchedulingAsync(
            Guid channelId,
            DateTimeOffset utcNow,
            CancellationToken cancellationToken)
        {
            ApproveForSchedulingCount++;
            var channel = UpdateChannel;
            return Task.FromResult(
                channel is not null && channel.TryBeginSchedulingAfterApproval(utcNow, approvedByUserId: Guid.NewGuid())
                    ? channel
                    : null);
        }

        public Task<Channel?> TryFailSchedulingAsync(
            Guid channelId,
            string safeReason,
            DateTimeOffset utcNow,
            CancellationToken cancellationToken)
        {
            FailSchedulingCount++;
            var channel = UpdateChannel;
            return Task.FromResult(
                channel is not null && channel.TryFailScheduling(safeReason, utcNow) ? channel : null);
        }

        public Task<IReadOnlyList<Guid>> LeaseStaleSchedulingAsync(
            DateTimeOffset staleBeforeUtc,
            DateTimeOffset utcNow,
            int maximumCount,
            CancellationToken cancellationToken)
        {
            LeaseStaleSchedulingCount++;
            ReceivedStaleBefore = staleBeforeUtc;
            ReceivedMaximumLeaseCount = maximumCount;
            return Task.FromResult(StaleSchedulingChannelIds);
        }
        public Task<ChannelApprovalResult> TryApproveForSchedulingAsync(Guid channelId, DateOnly quotaDate, Guid approvedByUserId, DateTimeOffset utcNow, CancellationToken cancellationToken)
        {
            ApproveForSchedulingCount++;
            ReceivedApprovalActorUserId = approvedByUserId;
            var channel = UpdateChannel;
            if (channel is not null && channel.TryBeginSchedulingAfterApproval(utcNow, approvedByUserId))
            {
                channel.FirstPageQuotaDate = quotaDate;
                return Task.FromResult(ChannelApprovalResult.Succeeded(channel));
            }

            return Task.FromResult(ChannelApprovalResult.Refused(ChannelApprovalStatus.NotPending));
        }

        public Task<ChannelBatchMutationResult> TryApproveManyForSchedulingAsync(IReadOnlyList<Guid> channelIds, DateOnly quotaDate, Guid approvedByUserId, DateTimeOffset utcNow, CancellationToken cancellationToken) =>
            Task.FromResult(ChannelBatchMutationResult.Refused(ChannelBatchMutationStatus.InvalidStatus));

        public Task<ChannelBatchMutationResult> TryRejectManyAsync(IReadOnlyList<Guid> channelIds, Guid rejectedByUserId, string reason, DateTimeOffset utcNow, CancellationToken cancellationToken) =>
            Task.FromResult(ChannelBatchMutationResult.Refused(ChannelBatchMutationStatus.InvalidStatus));

        public Task<ManualChannelCreateResult> CreateApprovedForSchedulingAsync(Channel channel, DateOnly quotaDate, CancellationToken cancellationToken) =>
            Task.FromResult(ManualChannelCreateResult.Created(channel));
}

    private sealed class FakeChannelActivityLogger : IChannelActivityLogger
    {
        public List<string> Events { get; } = [];

        public void LogApproved(Channel channel, ChannelStatus previousStatus) => Events.Add("Approved");

        public void LogRejected(Channel channel, ChannelStatus previousStatus) => Events.Add("Rejected");

        public void LogReopened(Channel channel) => Events.Add("Reopened");

        public void LogSoftDeleted(Channel channel, ChannelStatus previousStatus) => Events.Add("SoftDeleted");

        public void LogBackfillRetryRequested(Channel channel, BackfillStatus previousBackfillStatus) =>
            Events.Add($"BackfillRetryRequested:{previousBackfillStatus}");
    }

    /// <summary>
    /// Mirrors the observable scheduling outcome on the tracked entity so the approval/retry
    /// orchestration can be asserted without the real Hangfire adapter. The enqueue/error-transition
    /// rules themselves are proven in <c>BackfillSchedulingUseCaseTests</c>.
    /// </summary>
    private sealed class FakeBackfillSchedulingUseCase(FakeChannelRepository repository) : IBackfillSchedulingUseCase
    {
        public List<Guid> ScheduledChannelIds { get; } = [];

        public List<BackfillTriggerSource> TriggerSources { get; } = [];

        public BackfillStatus ResultingBackfillStatus { get; init; } = BackfillStatus.PENDING;

        public string ResultingFailureReason { get; init; } = BackfillSchedulingUseCase.SafeSchedulingFailureReason;

        public BackfillStatus? StatusWhenScheduled { get; private set; }

        public Task ScheduleAsync(
            Guid channelId,
            BackfillTriggerSource triggerSource,
            CancellationToken cancellationToken)
        {
            ScheduledChannelIds.Add(channelId);
            TriggerSources.Add(triggerSource);

            if (repository.ActiveChannel is { } channel)
            {
                StatusWhenScheduled = channel.BackfillStatus;
                channel.BackfillStatus = ResultingBackfillStatus;
                channel.BackfillSchedulingFailureReason =
                    ResultingBackfillStatus == BackfillStatus.SCHEDULING_FAILED ? ResultingFailureReason : null;
            }

            return Task.CompletedTask;
        }
    }

    private sealed class FixedTimeProvider(DateTimeOffset utcNow) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => utcNow;
    }

    /// <summary>Stub used by the existing lifecycle tests, which never call the manual-add flow.</summary>
    private sealed class FakeYouTubeChannelLookupGateway : IYouTubeChannelLookupGateway
    {
        public Task<ManualChannelLookupResult> LookupByHandleAsync(
            string handle,
            CancellationToken cancellationToken) =>
            Task.FromResult(ManualChannelLookupResult.NotFound());
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
