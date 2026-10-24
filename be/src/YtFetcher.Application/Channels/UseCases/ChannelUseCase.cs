using YtFetcher.Application.Abstractions.Repositories;
using YtFetcher.Application.Quotas;
using YtFetcher.Application.Abstractions.Services;
using YtFetcher.Application.Abstractions.UseCases;
using YtFetcher.Application.Channels.DTO.Request;
using YtFetcher.Application.Channels.DTO.Response;
using YtFetcher.Application.Channels.DTO.Result;
using YtFetcher.Application.Quotas.DTO.Result;
using YtFetcher.Core.BackfillRuns;
using YtFetcher.Core.Channels;

namespace YtFetcher.Application.Channels.UseCases;

public sealed class ChannelUseCase(
    IChannelRepository channelRepository,
    IChannelActivityLogger channelActivityLogger,
    IBackfillSchedulingUseCase backfillSchedulingUseCase,
    IDailyQuotaRepository dailyQuotaRepository,
    TimeProvider timeProvider,
    IYouTubeChannelLookupGateway youTubeChannelLookupGateway,
    ICurrentActor currentActor) : IChannelUseCase
{
    /// <summary>Number of newest evidence rows the review page shows per channel.</summary>
    private const int EvidenceLimit = 5;

    /// <summary>Readable message for any reference that is not a single bare <c>@handle</c>.</summary>
    private const string UnsupportedChannelReferenceMessage = "Enter a YouTube handle starting with @.";

    /// <summary>Readable message for a found channel whose uploads playlist is unavailable.</summary>
    private const string UploadsPlaylistUnavailableMessage =
        "This YouTube channel cannot be added because its uploads playlist is unavailable.";

    /// <summary>Durable status marker returned for a soft-deleted duplicate, which has lost its live status.</summary>
    public const string DeletedChannelStatus = "DELETED";

    public async Task<PendingChannelsResponse> GetPendingAsync(
        PendingChannelsRequest request,
        CancellationToken cancellationToken)
    {
        var searchText = NormalizeSearchText(request.SearchText);

        // The repository returns Core entities plus counts; building the response is an Application concern.
        var channels = await channelRepository.GetPendingPageAsync(
            request.Page,
            request.PageSize,
            searchText,
            cancellationToken);

        var totalCount = await channelRepository.CountPendingAsync(searchText, cancellationToken);
        var evidenceCounts = await channelRepository.CountEvidenceAsync(
            channels.Select(channel => channel.Id).ToArray(),
            cancellationToken);

        var items = channels
            .Select(channel => PendingChannelDto.ToDto(channel, evidenceCounts.GetValueOrDefault(channel.Id)))
            .ToList();

        return new PendingChannelsResponse(items, request.Page, request.PageSize, totalCount);
    }

    public async Task<ApprovedChannelsResponse> GetApprovedChannelsAsync(
        ApprovedChannelsRequest request,
        CancellationToken cancellationToken)
    {
        var (channels, totalCount) = await GetReviewChannelsAsync(
            ChannelStatus.APPROVED,
            request.Page,
            request.PageSize,
            request.SearchText,
            cancellationToken);

        return new ApprovedChannelsResponse(
            channels.Select(ApprovedChannelDto.ToDto).ToList(),
            request.Page,
            request.PageSize,
            totalCount);
    }

    public async Task<RejectedChannelsResponse> GetRejectedChannelsAsync(
        RejectedChannelsRequest request,
        CancellationToken cancellationToken)
    {
        var (channels, totalCount) = await GetReviewChannelsAsync(
            ChannelStatus.REJECTED,
            request.Page,
            request.PageSize,
            request.SearchText,
            cancellationToken);

        return new RejectedChannelsResponse(
            channels.Select(RejectedChannelDto.ToDto).ToList(),
            request.Page,
            request.PageSize,
            totalCount);
    }

    public async Task<ReviewCountsResponse> GetReviewCountsAsync(CancellationToken cancellationToken)
    {
        var counts = await channelRepository.CountChannelsByStatusAsync(cancellationToken);

        return new ReviewCountsResponse(
            counts.GetValueOrDefault(ChannelStatus.PENDING),
            counts.GetValueOrDefault(ChannelStatus.APPROVED),
            counts.GetValueOrDefault(ChannelStatus.REJECTED));
    }

    public async Task<ChannelEvidenceResponse?> GetEvidenceAsync(
        Guid channelId,
        CancellationToken cancellationToken)
    {
        var channel = await channelRepository.GetActiveAsync(channelId, cancellationToken);
        if (channel is null)
        {
            return null;
        }

        var evidence = await channelRepository.GetEvidenceAsync(channelId, EvidenceLimit, cancellationToken);
        if (evidence.Count == 0)
        {
            return new ChannelEvidenceResponse([]);
        }

        // One set-based keyword lookup for every distinct id in this page of evidence.
        var keywordIds = evidence
            .Select(discovery => discovery.KeywordId)
            .Distinct()
            .ToArray();

        var keywords = await channelRepository.GetDiscoveryKeywordsByIdsAsync(keywordIds, cancellationToken);

        var items = evidence
            .Select(discovery => ChannelEvidenceDto.ToDto(
                discovery,
                keywords.TryGetValue(discovery.KeywordId, out var keyword) ? keyword.Keyword : null))
            .ToList();

        return new ChannelEvidenceResponse(items);
    }

    public async Task<ChannelDetailDto?> GetByIdAsync(Guid channelId, CancellationToken cancellationToken)
    {
        var channel = await channelRepository.GetActiveAsync(channelId, cancellationToken);
        return channel is null ? null : ChannelDetailDto.ToDetail(channel);
    }

    public async Task<ChannelMutationResponse> ApproveAsync(
        Guid channelId,
        ApproveChannelRequest request,
        CancellationToken cancellationToken)
    {
        // The existing read path distinguishes an absent row from a lifecycle conflict before the
        // locked transition is attempted.
        var existing = await channelRepository.GetActiveAsync(channelId, cancellationToken);
        if (existing is null)
        {
            return new ChannelMutationResponse(ChannelMutationStatus.NotFound, null, null, null);
        }

        if (existing.Status != ChannelStatus.PENDING)
        {
            return Conflict(existing);
        }

        var approval = await channelRepository.TryApproveForSchedulingAsync(
            channelId,
            CurrentQuotaDate(),
            currentActor.UserId,
            timeProvider.GetUtcNow(),
            cancellationToken);

        if (approval.Status != ChannelApprovalStatus.Succeeded)
        {
            if (approval.Status == ChannelApprovalStatus.NotFound)
            {
                return new ChannelMutationResponse(ChannelMutationStatus.NotFound, null, null, null);
            }

            if (approval.Status == ChannelApprovalStatus.WindowNotReady)
            {
                return QuotaWindowNotReady();
            }

            if (approval.Status == ChannelApprovalStatus.LimitReached)
            {
                return ApprovalLimitReached();
            }

            // Another request changed the row between the read and the locked transition.
            var current = await channelRepository.GetActiveAsync(channelId, cancellationToken);
            return current is null
                ? new ChannelMutationResponse(ChannelMutationStatus.NotFound, null, null, null)
                : Conflict(current);
        }

        var channel = approval.Channel!;
        channelActivityLogger.LogApproved(channel, ChannelStatus.PENDING);

        // Approval is already committed, so the scheduling attempt runs outside any transaction. A
        // scheduling failure is persisted, never rolled back, and the response always reflects the
        // durable row rather than a server error.
        await backfillSchedulingUseCase.ScheduleAsync(
            channelId,
            BackfillTriggerSource.APPROVAL,
            cancellationToken);

        var persisted = await channelRepository.GetActiveAsync(channelId, cancellationToken) ?? channel;
        return new ChannelMutationResponse(
            ChannelMutationStatus.Succeeded,
            ChannelDetailDto.ToDetail(persisted),
            null,
            null);
    }

    public async Task<ChannelBatchMutationStatus> ApproveManyAsync(
        BulkApproveChannelsRequest request,
        CancellationToken cancellationToken)
    {
        var approval = await channelRepository.TryApproveManyForSchedulingAsync(
            request.ChannelIds!,
            CurrentQuotaDate(),
            currentActor.UserId,
            timeProvider.GetUtcNow(),
            cancellationToken);

        if (approval.Status != ChannelBatchMutationStatus.Succeeded)
        {
            return approval.Status;
        }

        foreach (var channel in approval.Channels)
        {
            channelActivityLogger.LogApproved(channel, ChannelStatus.PENDING);
            await backfillSchedulingUseCase.ScheduleAsync(
                channel.Id,
                BackfillTriggerSource.APPROVAL,
                cancellationToken);
        }

        return ChannelBatchMutationStatus.Succeeded;
    }

    public async Task<ChannelMutationResponse> ManualApproveAsync(
        ManualApproveChannelRequest request,
        CancellationToken cancellationToken)
    {
        if (!TryParseChannelReference(request.ChannelReference, out var handle))
        {
            return Validation(UnsupportedChannelReferenceMessage);
        }

        var quotaDate = CurrentQuotaDate();

        // One lookup, no search.list and no fallback. The lookup consumes non-reserved Queries capacity, so a
        // refusal is a readable 429/503 and YouTube is never called. Validation is deliberately not a
        // first-page approval attempt; the paired slot is claimed only when the channel is actually created.
        var lookupReservation = await dailyQuotaRepository.TryReserveQueriesUnitAsync(
            quotaDate,
            dailyProcessClaimToken: null,
            cancellationToken);
        if (lookupReservation != QuotaReservationResult.Reserved)
        {
            return lookupReservation == QuotaReservationResult.WindowNotReady
                ? QuotaWindowNotReady()
                : QuotaExceededToday();
        }

        var lookup = await youTubeChannelLookupGateway.LookupByHandleAsync(handle, cancellationToken);

        if (lookup.Status == ManualChannelLookupStatus.NotFound)
        {
            return new ChannelMutationResponse(ChannelMutationStatus.NotFound, null, null, null);
        }

        if (lookup.Status == ManualChannelLookupStatus.Unavailable)
        {
            return new ChannelMutationResponse(ChannelMutationStatus.ProviderUnavailable, null, null, null);
        }

        if (string.IsNullOrWhiteSpace(lookup.UploadsPlaylistId))
        {
            return Validation(UploadsPlaylistUnavailableMessage);
        }

        var utcNow = timeProvider.GetUtcNow();
        var channel = Channel.CreateApprovedForScheduling(
            lookup.YouTubeChannelId!,
            lookup.ChannelName!,
            lookup.ChannelThumbnailUrl,
            lookup.UploadsPlaylistId,
            utcNow,
            currentActor.UserId);

        // The create commits APPROVED + SCHEDULING in one short transaction before any enqueue, so
        // no job can observe a half-created channel. A Pending duplicate continues through the same approval
        // transaction as the review-list action; other lifecycle states remain conflicts.
        var creation = await channelRepository.CreateApprovedForSchedulingAsync(
            channel,
            quotaDate,
            cancellationToken);

        if (creation.Status == ChannelApprovalStatus.Duplicate)
        {
            var existing = creation.Channel!;
            if (existing.Status != ChannelStatus.PENDING)
            {
                return DuplicateConflict(existing);
            }

            var approval = await channelRepository.TryApproveForSchedulingAsync(
                existing.Id,
                quotaDate,
                currentActor.UserId,
                utcNow,
                cancellationToken);
            if (approval.Status != ChannelApprovalStatus.Succeeded)
            {
                return approval.Status == ChannelApprovalStatus.WindowNotReady
                    ? QuotaWindowNotReady()
                    : approval.Status == ChannelApprovalStatus.LimitReached
                        ? ApprovalLimitReached()
                        : Conflict((await channelRepository.GetActiveAsync(existing.Id, cancellationToken)) ?? existing);
            }

            channel = approval.Channel!;
            channelActivityLogger.LogApproved(channel, ChannelStatus.PENDING);
        }

        if (creation.Status is not (ChannelApprovalStatus.Succeeded or ChannelApprovalStatus.Duplicate))
        {
            // The paired slot claim is atomic with the insert; a refusal wrote no channel and no bucket unit.
            return creation.Status == ChannelApprovalStatus.WindowNotReady
                ? QuotaWindowNotReady()
                : ApprovalLimitReached();
        }

        // Reuse the existing scheduling path after the transaction commits, exactly like approval.
        await backfillSchedulingUseCase.ScheduleAsync(
            channel.Id,
            BackfillTriggerSource.APPROVAL,
            cancellationToken);

        var persisted = await channelRepository.GetActiveAsync(channel.Id, cancellationToken) ?? channel;
        return new ChannelMutationResponse(
            ChannelMutationStatus.Succeeded,
            ChannelDetailDto.ToDetail(persisted),
            null,
            null);
    }

    /// <summary>
    /// Operator retry of a terminally failed page. Only an active, approved channel whose backfill
    /// status is <see cref="BackfillStatus.FAILED"/> is retried. The execution-failure metadata is cleared
    /// and the channel returns to <see cref="BackfillStatus.PENDING"/> with a new ready time, while the
    /// claim, cursor, committed page count, cutoff, and quota reservation are deliberately untouched, so
    /// the retry resumes exactly the page that failed. Nothing is enqueued and no quota is reserved here:
    /// the dispatch pump picks the channel up like any other waiting channel.
    /// </summary>
    public async Task<ChannelMutationResponse> RetryBackfillAsync(
        Guid channelId,
        RetryBackfillRequest request,
        CancellationToken cancellationToken)
    {
        var channel = await channelRepository.GetActiveForUpdateAsync(channelId, cancellationToken);
        if (channel is null)
        {
            return new ChannelMutationResponse(ChannelMutationStatus.NotFound, null, null, null);
        }

        var previousBackfillStatus = channel.BackfillStatus;

        if (!channel.TryResetFailedBackfill(timeProvider.GetUtcNow(), currentActor.UserId))
        {
            return new ChannelMutationResponse(
                ChannelMutationStatus.Conflict,
                null,
                channel.Status.ToString(),
                null,
                channel.BackfillStatus.ToString());
        }

        await channelRepository.SaveChangesAsync(cancellationToken);
        channelActivityLogger.LogBackfillRetryRequested(channel, previousBackfillStatus);

        return new ChannelMutationResponse(
            ChannelMutationStatus.Succeeded,
            ChannelDetailDto.ToDetail(channel),
            null,
            null,
            channel.BackfillStatus.ToString());
    }

    public async Task<ChannelMutationResponse> ReopenAsync(
        Guid channelId,
        ReopenChannelRequest request,
        CancellationToken cancellationToken)
    {
        var channel = await channelRepository.GetActiveForUpdateAsync(channelId, cancellationToken);
        if (channel is null)
        {
            return new ChannelMutationResponse(ChannelMutationStatus.NotFound, null, null, null);
        }

        if (!channel.TryReopenForReview(timeProvider.GetUtcNow(), currentActor.UserId))
        {
            return new ChannelMutationResponse(
                ChannelMutationStatus.Conflict,
                null,
                channel.Status.ToString(),
                null,
                channel.BackfillStatus.ToString());
        }

        await channelRepository.SaveChangesAsync(cancellationToken);
        channelActivityLogger.LogReopened(channel);
        return new ChannelMutationResponse(ChannelMutationStatus.Succeeded, ChannelDetailDto.ToDetail(channel), null, null);
    }

    public async Task<ChannelMutationResponse> RejectAsync(
        Guid channelId,
        RejectChannelRequest request,
        CancellationToken cancellationToken)
    {
        var reason = request.Reason?.Trim();
        if (string.IsNullOrEmpty(reason))
        {
            return new ChannelMutationResponse(
                ChannelMutationStatus.ValidationError,
                null,
                null,
                "A rejection reason is required.");
        }

        if (reason.Length > Channel.RejectionReasonMaxLength)
        {
            return new ChannelMutationResponse(
                ChannelMutationStatus.ValidationError,
                null,
                null,
                $"A rejection reason cannot exceed {Channel.RejectionReasonMaxLength} characters.");
        }

        var channel = await channelRepository.GetActiveForUpdateAsync(channelId, cancellationToken);
        if (channel is null)
        {
            return new ChannelMutationResponse(ChannelMutationStatus.NotFound, null, null, null);
        }

        var previousStatus = channel.Status;
        if (!channel.Reject(timeProvider.GetUtcNow(), rejectedByUserId: currentActor.UserId, reason))
        {
            return new ChannelMutationResponse(ChannelMutationStatus.Conflict, null, channel.Status.ToString(), null);
        }

        await channelRepository.SaveChangesAsync(cancellationToken);
        channelActivityLogger.LogRejected(channel, previousStatus);

        return new ChannelMutationResponse(ChannelMutationStatus.Succeeded, ChannelDetailDto.ToDetail(channel), null, null);
    }

    public async Task<ChannelBatchMutationStatus> RejectManyAsync(
        BulkRejectChannelsRequest request,
        CancellationToken cancellationToken)
    {
        var reason = request.Reason!.Trim();
        var rejected = await channelRepository.TryRejectManyAsync(
            request.ChannelIds!,
            currentActor.UserId,
            reason,
            timeProvider.GetUtcNow(),
            cancellationToken);

        if (rejected.Status == ChannelBatchMutationStatus.Succeeded)
        {
            foreach (var channel in rejected.Channels)
            {
                channelActivityLogger.LogRejected(channel, rejected.PreviousStatuses[channel.Id]);
            }
        }

        return rejected.Status;
    }

    public async Task<ChannelMutationResponse> SoftDeleteAsync(
        Guid channelId,
        DeleteChannelRequest request,
        CancellationToken cancellationToken)
    {
        var channel = await channelRepository.GetActiveForUpdateAsync(channelId, cancellationToken);
        if (channel is null)
        {
            return new ChannelMutationResponse(ChannelMutationStatus.NotFound, null, null, null);
        }

        var previousStatus = channel.Status;
        if (!channel.SoftDelete(timeProvider.GetUtcNow(), deletedByUserId: currentActor.UserId))
        {
            return new ChannelMutationResponse(ChannelMutationStatus.Conflict, null, channel.Status.ToString(), null);
        }

        await channelRepository.SaveChangesAsync(cancellationToken);
        channelActivityLogger.LogSoftDeleted(channel, previousStatus);

        return new ChannelMutationResponse(ChannelMutationStatus.Succeeded, null, null, null);
    }

    /// <summary>
    /// Reads one review-ledger page. Only the approved and rejected ledgers exist, so any other status
    /// is rejected here, before the repository is called.
    /// </summary>
    private async Task<(IReadOnlyList<Channel> Channels, int TotalCount)> GetReviewChannelsAsync(
        ChannelStatus status,
        int page,
        int pageSize,
        string? searchText,
        CancellationToken cancellationToken)
    {
        if (status is not (ChannelStatus.APPROVED or ChannelStatus.REJECTED))
        {
            throw new ArgumentOutOfRangeException(
                nameof(status),
                status,
                "Only the approved and rejected channels are review ledgers.");
        }

        var normalizedSearchText = NormalizeSearchText(searchText);

        var channels = await channelRepository.GetReviewPageAsync(
            status,
            page,
            pageSize,
            normalizedSearchText,
            cancellationToken);

        var totalCount = await channelRepository.CountReviewChannelsAsync(
            status,
            normalizedSearchText,
            cancellationToken);

        return (channels, totalCount);
    }

    private static string? NormalizeSearchText(string? searchText)
    {
        var trimmed = searchText?.Trim();
        return string.IsNullOrEmpty(trimmed) ? null : trimmed;
    }

    private static ChannelMutationResponse Conflict(Channel channel) =>
        new(
            ChannelMutationStatus.Conflict,
            null,
            channel.Status.ToString(),
            null,
            channel.BackfillStatus.ToString());

    private static ChannelMutationResponse Validation(string message) =>
        new(ChannelMutationStatus.ValidationError, null, null, message);

    /// <summary>HTTP 429: the configured daily approved-channel maximum is already reached.</summary>
    private static ChannelMutationResponse ApprovalLimitReached() =>
        new(
            ChannelMutationStatus.QuotaExceededToday,
            null,
            null,
            "Maximum daily approved channels reached. Try again after the next quota reset.");

    /// <summary>HTTP 429: the ordinary Queries bucket is unavailable for today.</summary>
    private static ChannelMutationResponse QuotaExceededToday() =>
        new(
            ChannelMutationStatus.QuotaExceededToday,
            null,
            null,
            "YouTube request quota is exhausted for today. Try again after the next quota reset.");

    /// <summary>HTTP 503: Phase 0 has not initialized today's first-page window yet.</summary>
    private static ChannelMutationResponse QuotaWindowNotReady() =>
        new(
            ChannelMutationStatus.QuotaWindowNotReady,
            null,
            null,
            "The daily quota window has not started yet. Try again shortly.");

    private DateOnly CurrentQuotaDate() =>
        PacificQuotaWindow.GetQuotaDate(timeProvider.GetUtcNow());

    /// <summary>
    /// Reports an existing row that blocked a manual add without changing it. A soft-deleted row is
    /// reported as <see cref="DeletedChannelStatus"/> because its live lifecycle status no longer
    /// describes it.
    /// </summary>
    private static ChannelMutationResponse DuplicateConflict(Channel channel) =>
        new(
            ChannelMutationStatus.Conflict,
            null,
            channel.IsDeleted ? DeletedChannelStatus : channel.Status.ToString(),
            null,
            channel.BackfillStatus.ToString());

    /// <summary>
    /// Accepts exactly one bare <c>@handle</c>: a leading <c>@</c>, a non-empty handle, and no second
    /// <c>@</c>, whitespace, slash, query string, or fragment. URLs, channel IDs, and usernames are
    /// rejected before any provider call.
    /// </summary>
    private static bool TryParseChannelReference(string? channelReference, out string handle)
    {
        handle = string.Empty;

        var trimmed = channelReference?.Trim();
        if (string.IsNullOrEmpty(trimmed) || trimmed.Length < 2 || trimmed[0] != '@')
        {
            return false;
        }

        var name = trimmed[1..];
        if (name.Any(character => character is '@' or '/' or '?' or '#' || char.IsWhiteSpace(character)))
        {
            return false;
        }

        handle = trimmed;
        return true;
    }
}
