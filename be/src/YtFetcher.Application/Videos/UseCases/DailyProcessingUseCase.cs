using YtFetcher.Application.Abstractions.Repositories;
using YtFetcher.Application.Quotas;
using YtFetcher.Application.Abstractions.Services;
using YtFetcher.Application.Abstractions.UseCases;
using YtFetcher.Application.Backfill.DTO.Result;
using YtFetcher.Application.Quotas.DTO.Result;
using YtFetcher.Application.Videos.DTO.Result;
using YtFetcher.Core.BackfillRuns;
using YtFetcher.Core.Channels;
using YtFetcher.Core.Quotas;
using YtFetcher.Core.Videos;

namespace YtFetcher.Application.Videos.UseCases;

/// <summary>
/// The ordered daily processing phases: take durable ownership of the Pacific quota date (Phase 0 snapshots the
/// first-page window), refresh statistics for existing ACTIVE videos (M7), monitor newly uploaded videos (M6),
/// then complete the date and open the continuation gate only when both backfill buckets still have a positive
/// remainder.
/// </summary>
public sealed class DailyProcessingUseCase(
    IDailyQuotaRepository dailyQuotaRepository,
    IVideoStatisticsRepository videoStatisticsRepository,
    IYouTubeBackfillGateway youTubeBackfillGateway,
    INewUploadMonitoringRepository newUploadMonitoringRepository,
    INewUploadMonitorActivityLogger newUploadMonitorActivityLogger,
    IBackfillRepository backfillRepository,
    IBackfillPageLimitPolicy backfillPageLimitPolicy,
    IBackfillDispatchBatchSizePolicy backfillDispatchBatchSizePolicy,
    IBackfillSchedulingUseCase backfillSchedulingUseCase,
    TimeProvider timeProvider) : IRunDailyProcessingUseCase
{
    /// <summary>Provider-request bound of one statistics batch; one batch spends exactly one Batch Stats unit.</summary>
    private const int StatisticsBatchSize = 50;

    private const string MonitorResolveUploadsPlaylistOperation = "ResolveUploadsPlaylist";
    private const string MonitorGetUploadsPageOperation = "GetUploadsPage";
    private const string MonitorGetVideoStatsOperation = "GetVideoStats";
    private const string MonitorPersistOperation = "PersistUploadCheck";

    private const string MonitorPlaylistUnavailableReason =
        "Unable to find this channel's uploads playlist.";

    private const string MonitorUploadsFailureReason =
        "Unable to retrieve this channel's latest uploads from YouTube. Try again later.";

    public async Task<DailyProcessingResult> RunAsync(CancellationToken cancellationToken)
    {
        var utcNow = timeProvider.GetUtcNow();
        var quotaDate = PacificQuotaWindow.GetQuotaDate(utcNow);
        var quotaDateStartUtc = PacificQuotaWindow.GetQuotaDateStartUtc(quotaDate);

        // Phase 0: initialize the date's first-page window before anything may reserve a provider unit.
        await dailyQuotaRepository.EnsureLedgerAsync(quotaDate, cancellationToken);

        var claimToken = Guid.NewGuid();
        var decision = await dailyQuotaRepository.TryClaimDailyProcessAsync(
            quotaDate,
            claimToken,
            utcNow,
            cancellationToken);

        if (decision != DailyProcessClaimDecision.Claimed)
        {
            return DailyProcessingResult.NoOp(quotaDate, ToOutcome(decision));
        }

        try
        {
            return await RunOwnedAsync(quotaDate, quotaDateStartUtc, claimToken, utcNow, cancellationToken);
        }
        catch (OperationCanceledException)
        {
            await TryReleaseDailyClaimAsync(quotaDate, claimToken);
            throw;
        }
        catch
        {
            await TryReleaseDailyClaimAsync(quotaDate, claimToken);
            throw;
        }
    }

    private async Task<DailyProcessingResult> RunOwnedAsync(
        DateOnly quotaDate,
        DateTimeOffset quotaDateStartUtc,
        Guid claimToken,
        DateTimeOffset utcNow,
        CancellationToken cancellationToken)
    {
        await videoStatisticsRepository.ReleaseExpiredClaimsAsync(
            utcNow - Video.StatsClaimLifetime,
            utcNow,
            cancellationToken);

        var batchesSucceeded = 0;
        var videosUpdated = 0;
        var videosUnavailable = 0;
        var videoBatchStatsExhausted = false;

        // Phase 1 (M7): refresh statistics only for videos already stored in the database. It consumes only the
        // Video Batch Stats bucket.
        while (true)
        {
            var batch = await videoStatisticsRepository.ClaimDueBatchAsync(
                quotaDateStartUtc,
                utcNow,
                claimToken,
                StatisticsBatchSize,
                cancellationToken);

            if (batch.Count == 0)
            {
                break;
            }

            var claimedVideoIds = batch.Select(video => video.Id).ToArray();

            try
            {
                var reservation = await dailyQuotaRepository.TryReserveVideoBatchStatsUnitAsync(
                    quotaDate,
                    claimToken,
                    cancellationToken);

                if (reservation == QuotaReservationResult.OwnershipLost)
                {
                    // A replacement owns the date: release only this batch's claims, make no provider call, set
                    // no marker, and stop the run without finalizing.
                    await videoStatisticsRepository.ReleaseClaimsAsync(
                        claimedVideoIds,
                        claimToken,
                        utcNow,
                        cancellationToken);
                    return DailyProcessingResult.NoOp(quotaDate, DailyProcessingOutcome.OwnershipLost);
                }

                if (reservation != QuotaReservationResult.Reserved)
                {
                    // Expected limit: release the batch and let the date complete without a futile retry.
                    await videoStatisticsRepository.ReleaseClaimsAsync(
                        claimedVideoIds,
                        claimToken,
                        utcNow,
                        cancellationToken);
                    videoBatchStatsExhausted = true;
                    break;
                }

                var stats = await youTubeBackfillGateway.GetVideoStatsAsync(
                    batch.Select(video => video.YoutubeVideoId).ToArray(),
                    cancellationToken);

                if (stats.Outcome == BackfillProviderOutcome.QuotaExceeded)
                {
                    // The reservation stays spent: YouTube already received the request.
                    await dailyQuotaRepository.MarkVideoBatchStatsQuotaExhaustedAsync(
                        quotaDate,
                        timeProvider.GetUtcNow(),
                        cancellationToken);
                    await videoStatisticsRepository.ReleaseClaimsAsync(
                        claimedVideoIds,
                        claimToken,
                        utcNow,
                        cancellationToken);
                    videoBatchStatsExhausted = true;
                    break;
                }

                if (stats.Outcome != BackfillProviderOutcome.Succeeded)
                {
                    await videoStatisticsRepository.ReleaseClaimsAsync(
                        claimedVideoIds,
                        claimToken,
                        utcNow,
                        cancellationToken);
                    throw new DailyProcessingFailedException(stats.FailureReason ?? "provider failure");
                }

                var persisted = await videoStatisticsRepository.PersistSuccessAsync(
                    quotaDate,
                    claimToken,
                    claimedVideoIds,
                    claimToken,
                    stats.Items,
                    utcNow,
                    cancellationToken);

                if (!persisted.OwnershipRetained)
                {
                    // Another daily run recovered the stale claim while the provider call was in flight. The
                    // release is token-matched, so the surviving owner's claims are untouched, and the stale
                    // run stops as a no-op: no marker, no finalization, and no later daily phase.
                    await videoStatisticsRepository.ReleaseClaimsAsync(
                        claimedVideoIds,
                        claimToken,
                        utcNow,
                        cancellationToken);
                    return DailyProcessingResult.NoOp(quotaDate, DailyProcessingOutcome.OwnershipLost);
                }

                batchesSucceeded++;
                videosUpdated += persisted.Updated;
                videosUnavailable += persisted.Unavailable;
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (DailyProcessingFailedException)
            {
                throw;
            }
            catch
            {
                await TryReleaseBatchClaimsAsync(claimedVideoIds, claimToken);
                throw;
            }
        }

        // Phase 2 (M6): inspect the newest uploads page of every eligible channel. It is skipped when the Batch
        // Stats bucket is already unavailable, because an unknown upload can only be ingested by spending that
        // same bucket, so spending more Queries could not produce an ingestible result.
        var monitor = videoBatchStatsExhausted
            ? MonitorPhaseOutcome.Skipped
            : await RunNewUploadMonitorPhaseAsync(
                quotaDate,
                quotaDateStartUtc,
                claimToken,
                timeProvider.GetUtcNow(),
                cancellationToken);

        if (monitor.OwnershipLost)
        {
            // A stale daily owner must not finalize, open the gate, or dispatch on behalf of a replacement.
            return DailyProcessingResult.NoOp(quotaDate, DailyProcessingOutcome.OwnershipLost);
        }

        // Phase 3: complete the date and decide the gate from the live remainders in one owner-only statement.
        var finalization = await dailyQuotaRepository.FinalizeDailyProcessingAsync(
            quotaDate,
            claimToken,
            utcNow,
            cancellationToken);

        if (!finalization.Finalized)
        {
            return DailyProcessingResult.NoOp(quotaDate, DailyProcessingOutcome.AlreadyCompleted);
        }

        var ledger = await dailyQuotaRepository.GetForDateAsync(quotaDate, cancellationToken);
        var queriesExhausted = monitor.QueriesExhausted || (ledger?.IsQueriesQuotaExhausted ?? false);
        var batchExhausted = videoBatchStatsExhausted ||
                             monitor.VideoBatchStatsExhausted ||
                             (ledger?.IsVideoBatchStatsQuotaExhausted ?? false);

        if (!finalization.GateOpened)
        {
            return BuildResult(quotaDate, batchesSucceeded, videosUpdated, videosUnavailable, monitor, ledger,
                queriesExhausted, batchExhausted, gateOpened: false, 0, 0, 0);
        }

        // One pass is bounded by the configured dispatch size AND both live remainders, because each dispatched
        // continuation consumes one Query unit and, for a non-empty page, one Batch Stats unit.
        var totalBound = Math.Min(
            backfillDispatchBatchSizePolicy.BatchSize,
            Math.Min(finalization.QueriesRemainder, finalization.VideoBatchStatsRemainder));
        if (totalBound < 1)
        {
            return BuildResult(quotaDate, batchesSucceeded, videosUpdated, videosUnavailable, monitor, ledger,
                queriesExhausted, batchExhausted, gateOpened: true, 0, 0, 0);
        }

        var recovery = await backfillRepository.RecoverStaleWorkAsync(
            utcNow - Channel.BackfillClaimLifetime,
            utcNow,
            totalBound,
            cancellationToken);

        foreach (var channelId in recovery.RecoveredChannelIds)
        {
            await backfillSchedulingUseCase.ScheduleAsync(channelId, BackfillTriggerSource.RECOVERY, cancellationToken);
        }

        // Recovered and abandoned rows both return to the pending queue, so they count against the same bound.
        var remainingForFreshDispatch = Math.Max(
            0,
            totalBound - recovery.RecoveredChannelIds.Count - recovery.AbandonedRunCount);

        if (remainingForFreshDispatch < 1)
        {
            return BuildResult(quotaDate, batchesSucceeded, videosUpdated, videosUnavailable, monitor, ledger,
                queriesExhausted, batchExhausted, gateOpened: true,
                recovery.RecoveredChannelIds.Count, recovery.AbandonedRunCount, 0);
        }

        var dispatch = await backfillRepository.DispatchCandidatesAsync(
            backfillPageLimitPolicy.PageLimit,
            utcNow,
            remainingForFreshDispatch,
            cancellationToken);

        foreach (var channelId in dispatch.DispatchedChannelIds)
        {
            await backfillSchedulingUseCase.ScheduleAsync(channelId, BackfillTriggerSource.DAILY_PUMP, cancellationToken);
        }

        return BuildResult(quotaDate, batchesSucceeded, videosUpdated, videosUnavailable, monitor, ledger,
            queriesExhausted, batchExhausted, gateOpened: true,
            recovery.RecoveredChannelIds.Count, recovery.AbandonedRunCount, dispatch.DispatchedChannelIds.Count);
    }

    /// <summary>
    /// M6 monitoring: reads the newest uploads page of every eligible channel in deterministic order, reserves a
    /// Queries unit before each playlist request and a Video Batch Stats unit before each unknown-upload lookup,
    /// and advances the channel's success marker only after the page has persisted. A provider limit stops the
    /// phase and records only the matching bucket marker; a non-quota provider or persistence failure fails the
    /// run so a same-date retry can revisit the channel.
    /// </summary>
    private async Task<MonitorPhaseOutcome> RunNewUploadMonitorPhaseAsync(
        DateOnly quotaDate,
        DateTimeOffset quotaDateStartUtc,
        Guid claimToken,
        DateTimeOffset phaseStartedAtUtc,
        CancellationToken cancellationToken)
    {
        var channels = await newUploadMonitoringRepository.GetUploadMonitorCandidatesAsync(
            quotaDateStartUtc,
            cancellationToken);

        var channelsChecked = 0;
        var channelsWithNewVideos = 0;
        var videosInserted = 0;
        var knownVideosSkipped = 0;
        var otherVideosSkipped = 0;

        MonitorPhaseOutcome Stopped(bool queriesExhausted, bool batchStatsExhausted) => new(
            OwnershipLost: false,
            queriesExhausted,
            batchStatsExhausted,
            channelsChecked,
            channelsWithNewVideos,
            videosInserted,
            knownVideosSkipped,
            otherVideosSkipped);

        MonitorPhaseOutcome OwnershipStopped() => new(
            OwnershipLost: true,
            QueriesExhausted: false,
            VideoBatchStatsExhausted: false,
            channelsChecked,
            channelsWithNewVideos,
            videosInserted,
            knownVideosSkipped,
            otherVideosSkipped);

        foreach (var channel in channels)
        {
            var uploadsPlaylistId = channel.UploadsPlaylistId;

            if (string.IsNullOrWhiteSpace(uploadsPlaylistId))
            {
                var playlistReservation = await dailyQuotaRepository.TryReserveQueriesUnitAsync(
                    quotaDate,
                    claimToken,
                    cancellationToken);

                if (playlistReservation == QuotaReservationResult.OwnershipLost)
                {
                    return OwnershipStopped();
                }

                if (playlistReservation != QuotaReservationResult.Reserved)
                {
                    return Stopped(queriesExhausted: true, batchStatsExhausted: false);
                }

                var playlist = await youTubeBackfillGateway.ResolveUploadsPlaylistAsync(
                    channel.YoutubeChannelId,
                    cancellationToken);

                if (playlist.Outcome == BackfillProviderOutcome.QuotaExceeded)
                {
                    await dailyQuotaRepository.MarkQueriesQuotaExhaustedAsync(
                        quotaDate,
                        timeProvider.GetUtcNow(),
                        cancellationToken);
                    return Stopped(queriesExhausted: true, batchStatsExhausted: false);
                }

                if (playlist.Outcome != BackfillProviderOutcome.Succeeded ||
                    string.IsNullOrWhiteSpace(playlist.UploadsPlaylistId))
                {
                    newUploadMonitorActivityLogger.LogProviderFailed(
                        channel.Id,
                        MonitorResolveUploadsPlaylistOperation,
                        MonitorPlaylistUnavailableReason);
                    throw new DailyProcessingFailedException(MonitorPlaylistUnavailableReason);
                }

                uploadsPlaylistId = playlist.UploadsPlaylistId;
            }

            var pageReservation = await dailyQuotaRepository.TryReserveQueriesUnitAsync(
                quotaDate,
                claimToken,
                cancellationToken);

            if (pageReservation == QuotaReservationResult.OwnershipLost)
            {
                return OwnershipStopped();
            }

            if (pageReservation != QuotaReservationResult.Reserved)
            {
                return Stopped(queriesExhausted: true, batchStatsExhausted: false);
            }

            var page = await youTubeBackfillGateway.GetUploadsPageAsync(
                uploadsPlaylistId,
                pageToken: null,
                cancellationToken);

            if (page.Outcome == BackfillProviderOutcome.QuotaExceeded)
            {
                await dailyQuotaRepository.MarkQueriesQuotaExhaustedAsync(
                    quotaDate,
                    timeProvider.GetUtcNow(),
                    cancellationToken);
                return Stopped(queriesExhausted: true, batchStatsExhausted: false);
            }

            if (page.Outcome != BackfillProviderOutcome.Succeeded)
            {
                newUploadMonitorActivityLogger.LogProviderFailed(
                    channel.Id,
                    MonitorGetUploadsPageOperation,
                    MonitorUploadsFailureReason);
                throw new DailyProcessingFailedException(MonitorUploadsFailureReason);
            }

            var seen = new HashSet<string>(StringComparer.Ordinal);
            var candidateItems = new List<UploadsPageItemResult>();
            foreach (var item in page.Items)
            {
                if (string.IsNullOrWhiteSpace(item.YoutubeVideoId) || item.PublishedAt is null)
                {
                    continue;
                }

                if (channel.LastUploadCheckAt is { } marker && item.PublishedAt <= marker)
                {
                    continue;
                }

                if (seen.Add(item.YoutubeVideoId))
                {
                    candidateItems.Add(item);
                }
            }

            var knownIds = await newUploadMonitoringRepository.GetKnownYoutubeVideoIdsAsync(
                candidateItems.Select(item => item.YoutubeVideoId).ToArray(),
                cancellationToken);
            var unknownItems = candidateItems.Where(item => !knownIds.Contains(item.YoutubeVideoId)).ToList();
            var preCallKnownSkipped = candidateItems.Count - unknownItems.Count;

            var matchedItems = new List<NewUploadCheckItem>();
            var pageOtherSkipped = 0;

            if (unknownItems.Count > 0)
            {
                var statsReservation = await dailyQuotaRepository.TryReserveVideoBatchStatsUnitAsync(
                    quotaDate,
                    claimToken,
                    cancellationToken);

                if (statsReservation == QuotaReservationResult.OwnershipLost)
                {
                    return OwnershipStopped();
                }

                if (statsReservation != QuotaReservationResult.Reserved)
                {
                    return Stopped(queriesExhausted: false, batchStatsExhausted: true);
                }

                var stats = await youTubeBackfillGateway.GetVideoStatsAsync(
                    unknownItems.Select(item => item.YoutubeVideoId).ToArray(),
                    cancellationToken);

                if (stats.Outcome == BackfillProviderOutcome.QuotaExceeded)
                {
                    await dailyQuotaRepository.MarkVideoBatchStatsQuotaExhaustedAsync(
                        quotaDate,
                        timeProvider.GetUtcNow(),
                        cancellationToken);
                    return Stopped(queriesExhausted: false, batchStatsExhausted: true);
                }

                if (stats.Outcome != BackfillProviderOutcome.Succeeded)
                {
                    newUploadMonitorActivityLogger.LogProviderFailed(
                        channel.Id,
                        MonitorGetVideoStatsOperation,
                        MonitorUploadsFailureReason);
                    throw new DailyProcessingFailedException(MonitorUploadsFailureReason);
                }

                var statsByVideoId = new Dictionary<string, VideoStatsItemResult>(StringComparer.Ordinal);
                foreach (var statsItem in stats.Items)
                {
                    if (!string.IsNullOrWhiteSpace(statsItem.YoutubeVideoId))
                    {
                        statsByVideoId.TryAdd(statsItem.YoutubeVideoId, statsItem);
                    }
                }

                foreach (var item in unknownItems)
                {
                    if (!statsByVideoId.TryGetValue(item.YoutubeVideoId, out var statsItem) ||
                        statsItem.DurationMillis < 0 ||
                        !Video.IsShortForm(statsItem.DurationMillis))
                    {
                        pageOtherSkipped++;
                        continue;
                    }

                    matchedItems.Add(new NewUploadCheckItem(
                        item.YoutubeVideoId,
                        item.Title,
                        item.ThumbnailUrl,
                        item.PublishedAt!.Value,
                        statsItem.DurationMillis,
                        statsItem.ViewCount,
                        statsItem.LikeCount));
                }
            }

            NewUploadCheckPersistResult persisted;
            try
            {
                persisted = await newUploadMonitoringRepository.PersistUploadCheckAsync(
                    new NewUploadCheckRequest(
                        quotaDate,
                        channel.Id,
                        claimToken,
                        phaseStartedAtUtc,
                        channel.UploadsPlaylistId,
                        matchedItems),
                    cancellationToken);
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception exception)
            {
                newUploadMonitorActivityLogger.LogPersistenceFailed(
                    channel.Id,
                    MonitorPersistOperation,
                    exception);
                throw new DailyProcessingFailedException("new upload monitor could not persist a channel page");
            }

            if (!persisted.OwnershipRetained)
            {
                // Same expected stale-owner case as M7: the run stops and the outer owned run maps it to a no-op.
                return OwnershipStopped();
            }

            if (!persisted.ChannelStillEligible)
            {
                continue;
            }

            channelsChecked++;
            if (persisted.InsertedCount > 0)
            {
                channelsWithNewVideos++;
            }

            videosInserted += persisted.InsertedCount;
            knownVideosSkipped += preCallKnownSkipped + persisted.SkippedKnownCount;
            otherVideosSkipped += pageOtherSkipped;
        }

        return new MonitorPhaseOutcome(
            OwnershipLost: false,
            QueriesExhausted: false,
            VideoBatchStatsExhausted: false,
            channelsChecked,
            channelsWithNewVideos,
            videosInserted,
            knownVideosSkipped,
            otherVideosSkipped);
    }

    private static DailyProcessingResult BuildResult(
        DateOnly quotaDate,
        int batchesSucceeded,
        int videosUpdated,
        int videosUnavailable,
        MonitorPhaseOutcome monitor,
        YouTubeDailyQuotaUsage? ledger,
        bool queriesExhausted,
        bool videoBatchStatsExhausted,
        bool gateOpened,
        int recoveredChannels,
        int abandonedRuns,
        int channelsDispatched) =>
        new(
            quotaDate,
            DailyProcessingOutcome.Completed,
            batchesSucceeded,
            videosUpdated,
            videosUnavailable,
            monitor.ChannelsChecked,
            monitor.ChannelsWithNewVideos,
            monitor.VideosInserted,
            monitor.KnownVideosSkipped,
            monitor.OtherVideosSkipped,
            ledger?.QueriesUnitsUsed ?? 0,
            ledger?.VideoBatchStatsUnitsUsed ?? 0,
            ledger?.SearchQueryUnitsUsed ?? 0,
            queriesExhausted,
            videoBatchStatsExhausted,
            gateOpened,
            recoveredChannels,
            abandonedRuns,
            channelsDispatched);

    private static DailyProcessingOutcome ToOutcome(DailyProcessClaimDecision decision) => decision switch
    {
        DailyProcessClaimDecision.AlreadyCompleted => DailyProcessingOutcome.AlreadyCompleted,
        _ => DailyProcessingOutcome.AlreadyRunning
    };

    /// <summary>
    /// Releases the daily-process claim with the strongest available cancellation token, so a cancelled or
    /// failing run does not keep the date blocked when the release can still be written.
    /// </summary>
    private async Task TryReleaseDailyClaimAsync(DateOnly quotaDate, Guid claimToken)
    {
        try
        {
            await dailyQuotaRepository.ReleaseDailyProcessClaimAsync(quotaDate, claimToken, CancellationToken.None);
        }
        catch
        {
            // Best effort only: the durable claim is then recovered by its two-hour stale boundary.
        }
    }

    /// <summary>
    /// Best-effort token-matched release of the current batch, used when persistence failed unexpectedly. A
    /// failure here must not replace the original exception.
    /// </summary>
    private async Task TryReleaseBatchClaimsAsync(IReadOnlyList<Guid> claimedVideoIds, Guid claimToken)
    {
        try
        {
            await videoStatisticsRepository.ReleaseClaimsAsync(
                claimedVideoIds,
                claimToken,
                timeProvider.GetUtcNow(),
                CancellationToken.None);
        }
        catch
        {
            // Best effort only: the video claim lifetime also recovers these rows.
        }
    }

    private sealed record MonitorPhaseOutcome(
        bool OwnershipLost,
        bool QueriesExhausted,
        bool VideoBatchStatsExhausted,
        int ChannelsChecked,
        int ChannelsWithNewVideos,
        int VideosInserted,
        int KnownVideosSkipped,
        int OtherVideosSkipped)
    {
        /// <summary>M6 was skipped because the Batch Stats bucket was already unavailable for this date.</summary>
        public static MonitorPhaseOutcome Skipped => new(false, false, true, 0, 0, 0, 0, 0);
    }
}
