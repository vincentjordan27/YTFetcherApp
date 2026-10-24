using YtFetcher.Application.Abstractions.Repositories;
using YtFetcher.Application.Quotas;
using YtFetcher.Application.Abstractions.Services;
using YtFetcher.Application.Abstractions.UseCases;
using YtFetcher.Application.Backfill.DTO.Result;
using YtFetcher.Application.Quotas.DTO.Result;
using YtFetcher.Core.BackfillRuns;
using YtFetcher.Core.Channels;

namespace YtFetcher.Application.Backfill.UseCases;

/// <summary>
/// Coordinates one first-page or continuation backfill attempt: claim, the per-bucket quota reservation that
/// must precede each provider call, and the one page-persistence transaction. Expected provider, quota, and
/// persistence outcomes are handled here and return normally.
/// </summary>
public sealed class BackfillUseCase(
    IBackfillRepository backfillRepository,
    IYouTubeBackfillGateway youTubeBackfillGateway,
    IDailyQuotaRepository dailyQuotaRepository,
    IBackfillActivityLogger backfillActivityLogger,
    IBackfillPageLimitPolicy backfillPageLimitPolicy,
    TimeProvider timeProvider) : IBackfillUseCase
{
    /// <summary>Safe operator-facing reason for a failed page-persistence transaction.</summary>
    private const string PersistenceFailureReason = "Unable to save backfill results. Try again later.";

    private const string ValidatePageOperation = "ValidatePage";
    private const string GetUploadsPageOperation = "GetUploadsPage";
    private const string GetVideoStatsOperation = "GetVideoStats";
    private const string PersistPageOperation = "PersistPage";

    private const string MissingUploadsPlaylistFailureReason =
        "This channel cannot be backfilled because its uploads playlist is missing.";

    private const string UploadsPageFailureReason =
        "Unable to retrieve uploads from YouTube. Try again later.";

    private const string VideoStatsFailureReason =
        "Unable to retrieve video statistics from YouTube. Try again later.";

    /// <summary>Fallback sentence, so even an unexpected operation stays readable for the operator.</summary>
    private const string UnexpectedFailureReason =
        "Unable to complete this channel's backfill. Try again later.";

    /// <summary>
    /// Fixed, secret-free message for the retry-request exception raised when a committed execution
    /// failure is below the failure limit.
    /// </summary>
    private const string RetryRequestedMessage =
        "The backfill attempt failed and was recorded. A retry is required.";

    /// <summary>
    /// The uploads page is requested with <c>maxResults=50</c>; anything beyond that is a provider
    /// contract violation and is trimmed so one statistics call can never exceed the batch size.
    /// </summary>
    private const int MaxUploadsPerPage = 50;

    public async Task RunPageAsync(
        Guid channelId,
        BackfillTriggerSource triggerSource,
        CancellationToken cancellationToken)
    {
        var claimToken = Guid.NewGuid();
        var runStartedAt = timeProvider.GetUtcNow();
        var quotaDate = PacificQuotaWindow.GetQuotaDate(runStartedAt);

        var channel = await backfillRepository.TryClaimPageAsync(
            channelId,
            claimToken,
            quotaDate,
            triggerSource,
            runStartedAt,
            cancellationToken);

        if (channel is null)
        {
            return;
        }

        var isFirstPage = channel.BackfillPagesCompleted == 0;

        // The active lifecycle guarantees an approved, backfill-eligible channel already has an uploads
        // playlist id. A legacy or corrupt row is a readable operational failure with no provider call and no
        // quota spend; there is deliberately no fallback channels.list request here.
        if (string.IsNullOrWhiteSpace(channel.UploadsPlaylistId))
        {
            await RecordFailureAsync(
                channelId,
                claimToken,
                ValidatePageOperation,
                MissingUploadsPlaylistFailureReason,
                cancellationToken);
            return;
        }

        if (!isFirstPage)
        {
            // A continuation reserves its own ordinary Queries unit immediately before the playlist call, with no
            // daily-owner fence. A page-one attempt already holds its paired reservation, claimed by the
            // repository inside the claim transaction (including the rollover case), so it takes no bucket unit
            // again.
            var queriesReservation = await dailyQuotaRepository.TryReserveQueriesUnitAsync(
                quotaDate,
                dailyProcessClaimToken: null,
                cancellationToken);

            if (queriesReservation != QuotaReservationResult.Reserved)
            {
                await backfillRepository.DeferForQuotaLimitAsync(
                    channelId,
                    claimToken,
                    QuotaLimitBucket.QUERIES,
                    timeProvider.GetUtcNow(),
                    cancellationToken);
                return;
            }
        }

        var page = await youTubeBackfillGateway.GetUploadsPageAsync(
            channel.UploadsPlaylistId,
            channel.BackfillPageToken,
            cancellationToken);

        if (page.Outcome == BackfillProviderOutcome.QuotaExceeded)
        {
            await MarkBucketExhaustedAndDeferAsync(
                channelId,
                claimToken,
                quotaDate,
                QuotaLimitBucket.QUERIES,
                cancellationToken);
            return;
        }

        if (page.Outcome != BackfillProviderOutcome.Succeeded)
        {
            // The Query reservation already spent is deliberately kept: the request reached YouTube.
            await RecordProviderFailureAsync(
                channelId,
                claimToken,
                GetUploadsPageOperation,
                page.FailureReason,
                cancellationToken);
            return;
        }

        // Malformed entries and duplicate ids are discarded while provider order is preserved.
        var uploadItems = new List<UploadsPageItemResult>();
        var seenVideoIds = new HashSet<string>(StringComparer.Ordinal);

        foreach (var item in page.Items)
        {
            if (string.IsNullOrWhiteSpace(item.YoutubeVideoId) || !seenVideoIds.Add(item.YoutubeVideoId))
            {
                continue;
            }

            uploadItems.Add(item);
        }

        if (uploadItems.Count > MaxUploadsPerPage)
        {
            uploadItems = uploadItems.Take(MaxUploadsPerPage).ToList();
        }

        // The product boundary is the frozen cutoff, never the cursor: a page that walked past the horizon
        // is the last page even when YouTube still offers a next token.
        var effectiveCutoff = channel.BackfillCutoffAt ?? runStartedAt.AddDays(-Channel.BackfillHorizonDays);
        var crossedCutoff = uploadItems.Any(
            item => item.PublishedAt is { } publishedAt && publishedAt < effectiveCutoff);

        var pageLimit = backfillPageLimitPolicy.PageLimit;
        var reachedPageLimit = pageLimit > 0 && channel.BackfillPagesCompleted + 1 >= pageLimit;
        var isChannelComplete = page.NextPageToken is null || crossedCutoff || reachedPageLimit;

        // A page without usable ids persists completion/cursor without reserving or calling Batch Stats.
        if (uploadItems.Count == 0)
        {
            await PersistPageOrRecordFailureAsync(
                new BackfillPageResult(
                    channelId,
                    claimToken,
                    channel.UploadsPlaylistId,
                    uploadItems,
                    [],
                    page.NextPageToken,
                    isChannelComplete,
                    runStartedAt),
                cancellationToken);
            return;
        }

        // A non-empty page needs the Batch Stats bucket. Page one already holds its approval-time unit; a
        // continuation reserves one immediately before this call, never before the playlist result is known.
        if (!isFirstPage)
        {
            var statsReservation = await dailyQuotaRepository.TryReserveVideoBatchStatsUnitAsync(
                quotaDate,
                dailyProcessClaimToken: null,
                cancellationToken);

            if (statsReservation != QuotaReservationResult.Reserved)
            {
                await backfillRepository.DeferForQuotaLimitAsync(
                    channelId,
                    claimToken,
                    QuotaLimitBucket.VIDEO_BATCH_STATS,
                    timeProvider.GetUtcNow(),
                    cancellationToken);
                return;
            }
        }

        var stats = await youTubeBackfillGateway.GetVideoStatsAsync(
            uploadItems.Select(item => item.YoutubeVideoId).ToArray(),
            cancellationToken);

        if (stats.Outcome == BackfillProviderOutcome.QuotaExceeded)
        {
            await MarkBucketExhaustedAndDeferAsync(
                channelId,
                claimToken,
                quotaDate,
                QuotaLimitBucket.VIDEO_BATCH_STATS,
                cancellationToken);
            return;
        }

        if (stats.Outcome != BackfillProviderOutcome.Succeeded)
        {
            await RecordProviderFailureAsync(
                channelId,
                claimToken,
                GetVideoStatsOperation,
                stats.FailureReason,
                cancellationToken);
            return;
        }

        var statsByVideoId = new Dictionary<string, VideoStatsItemResult>(StringComparer.Ordinal);
        foreach (var item in stats.Items)
        {
            if (!string.IsNullOrWhiteSpace(item.YoutubeVideoId))
            {
                statsByVideoId.TryAdd(item.YoutubeVideoId, item);
            }
        }

        // Only entries YouTube described completely take part in the page write.
        var matchedStats = new List<VideoStatsItemResult>();
        foreach (var item in uploadItems)
        {
            if (item.PublishedAt is null ||
                !statsByVideoId.TryGetValue(item.YoutubeVideoId, out var statsItem) ||
                statsItem.DurationMillis < 0)
            {
                continue;
            }

            matchedStats.Add(statsItem);
        }

        await PersistPageOrRecordFailureAsync(
            new BackfillPageResult(
                channelId,
                claimToken,
                channel.UploadsPlaylistId,
                uploadItems,
                matchedStats,
                page.NextPageToken,
                isChannelComplete,
                runStartedAt),
            cancellationToken);
    }

    /// <summary>
    /// Marks the bucket that YouTube reported exhausted for this Pacific date, then closes this attempt as a
    /// quota limit. The channel stays eligible for the next quota day instead of entering the failure-retry
    /// path, because an expected provider limit is not an execution failure.
    /// </summary>
    private async Task MarkBucketExhaustedAndDeferAsync(
        Guid channelId,
        Guid claimToken,
        DateOnly quotaDate,
        QuotaLimitBucket bucket,
        CancellationToken cancellationToken)
    {
        var utcNow = timeProvider.GetUtcNow();

        if (bucket == QuotaLimitBucket.QUERIES)
        {
            await dailyQuotaRepository.MarkQueriesQuotaExhaustedAsync(quotaDate, utcNow, cancellationToken);
        }
        else
        {
            await dailyQuotaRepository.MarkVideoBatchStatsQuotaExhaustedAsync(quotaDate, utcNow, cancellationToken);
        }

        backfillActivityLogger.LogFirstPageQuotaExceeded(channelId, quotaDate, bucket.ToString());

        await backfillRepository.DeferForQuotaLimitAsync(
            channelId,
            claimToken,
            bucket,
            utcNow,
            cancellationToken);
    }

    private async Task RecordProviderFailureAsync(
        Guid channelId,
        Guid claimToken,
        string operation,
        string? providerDetail,
        CancellationToken cancellationToken)
    {
        // Provider text is a diagnostic for the log only. The persisted reason is always the fixed,
        // readable sentence for this operation, truncated defensively for the log line.
        backfillActivityLogger.LogFirstPageProviderFailed(
            channelId,
            operation,
            SanitizeProviderDetail(providerDetail));

        await RecordFailureAsync(
            channelId,
            claimToken,
            operation,
            SafeFailureReason(operation),
            cancellationToken);
    }

    private async Task RecordFailureAsync(
        Guid channelId,
        Guid claimToken,
        string operation,
        string reason,
        CancellationToken cancellationToken)
    {
        BackfillFailureResult? failure;

        try
        {
            failure = await backfillRepository.RecordFailureAsync(
                channelId,
                claimToken,
                reason,
                timeProvider.GetUtcNow(),
                cancellationToken);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            // The failure-state transaction did not commit, so no release happened. The unexpected
            // exception is logged and rethrown; the live claim expires after its 15 minute lifetime.
            backfillActivityLogger.LogFailureStateWriteFailed(channelId, operation, exception);
            throw;
        }

        if (failure is null)
        {
            return;
        }

        backfillActivityLogger.LogFirstPageFailed(
            channelId,
            failure.FailureCount,
            Channel.BackfillFailureLimit,
            reason);

        if (failure.ReachedFailureLimit)
        {
            // Terminal page work: the channel keeps its cursor, committed count and data, and the pump stops
            // selecting it. Only an explicit operator retry returns it to the ordinary path.
            backfillActivityLogger.LogTerminalPageFailure(channelId, failure.FailureCount, reason);
            return;
        }

        if (!failure.ShouldRequestRetry)
        {
            // The failure is preserved in Backfill Runs, but a rejected/deleted channel is no longer eligible.
            // Returning prevents Hangfire from recording a misleading successful no-op retry.
            return;
        }

        // The durable failure transition committed and the channel remains eligible, so this Hangfire job must
        // run again after its configured delay and re-claim the same durable cursor.
        throw new BackfillRetryRequestedException(RetryRequestedMessage);
    }

    private async Task PersistPageOrRecordFailureAsync(
        BackfillPageResult page,
        CancellationToken cancellationToken)
    {
        try
        {
            await backfillRepository.PersistPageAsync(page, cancellationToken);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            // The page transaction rolled back. The original exception is logged for diagnosis, then
            // only the fixed user-safe reason is persisted in its own transaction.
            backfillActivityLogger.LogPersistenceFailed(page.ChannelId, PersistPageOperation, exception);

            await RecordFailureAsync(
                page.ChannelId,
                page.ClaimToken,
                PersistPageOperation,
                PersistenceFailureReason,
                cancellationToken);
        }
    }

    /// <summary>
    /// Fixed readable sentence for the persisted failure reason. Raw gateway text is never persisted
    /// and the value is never "UnknownFailure".
    /// </summary>
    private static string SafeFailureReason(string operation) => operation switch
    {
        GetUploadsPageOperation => UploadsPageFailureReason,
        GetVideoStatsOperation => VideoStatsFailureReason,
        PersistPageOperation => PersistenceFailureReason,
        _ => UnexpectedFailureReason
    };

    /// <summary>Provider diagnostics stay in the log only, with control characters removed and a bounded length.</summary>
    private static string SanitizeProviderDetail(string? providerDetail)
    {
        if (string.IsNullOrWhiteSpace(providerDetail))
        {
            return "UnknownFailure";
        }

        var sanitized = new string(providerDetail
            .Where(character => !char.IsControl(character))
            .ToArray())
            .Trim();

        return sanitized.Length <= Channel.BackfillFailureReasonMaxLength
            ? sanitized
            : sanitized[..Channel.BackfillFailureReasonMaxLength];
    }
}
