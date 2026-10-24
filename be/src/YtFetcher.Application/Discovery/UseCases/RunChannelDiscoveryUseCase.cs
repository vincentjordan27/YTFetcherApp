using YtFetcher.Application.Abstractions.Repositories;
using YtFetcher.Application.Quotas;
using YtFetcher.Application.Abstractions.Services;
using YtFetcher.Application.Abstractions.UseCases;
using YtFetcher.Application.Discovery.DTO.Result;
using YtFetcher.Application.Quotas.DTO.Result;
using YtFetcher.Core.Discovery;

namespace YtFetcher.Application.Discovery.UseCases;

/// <summary>
/// Reserves Search before each <c>search.list</c> and Queries before each <c>channels.list</c> chunk.
/// Successful pages advance their own keyword cursor; provider or persistence failures leave it unchanged.
/// </summary>
public sealed class RunChannelDiscoveryUseCase(
    IDiscoveryRepository discoveryRepository,
    IYouTubeChannelDiscoveryGateway youTubeChannelDiscoveryGateway,
    IDailyQuotaRepository dailyQuotaRepository,
    IDiscoveryActivityLogger discoveryActivityLogger,
    TimeProvider timeProvider,
    IDiscoveryPageLimitPolicy pageLimitPolicy) : IRunChannelDiscoveryUseCase
{
    private const int ChannelMetadataChunkSize = 50;
    private const string QuotaLimitReachedReason = "QuotaLimitReached";

    public async Task<ChannelDiscoveryRunSummary> RunAsync(CancellationToken cancellationToken)
    {
        var startedAt = timeProvider.GetUtcNow();
        var quotaDate = PacificQuotaWindow.GetQuotaDate(startedAt);
        var pageLimit = pageLimitPolicy.PageLimit;

        var keywords = await discoveryRepository.GetActiveKeywordsAsync(cancellationToken);
        var states = await discoveryRepository.GetStatesForKeywordsAsync(
            keywords.Select(keyword => keyword.Id).ToList(),
            cancellationToken);
        var stateByKeywordId = states.ToDictionary(state => state.KeywordId);

        var pageCountByKeywordId = new Dictionary<Guid, int>();
        var failedKeywordIds = new HashSet<Guid>();
        var terminalKeywordIds = states
            .Where(state => state.RecentCompletedQuotaDate == quotaDate)
            .Select(state => state.KeywordId)
            .ToHashSet();
        var successfulKeywordIds = new HashSet<Guid>();
        var failedKeywords = 0;
        var searchItemsReceived = 0;
        var malformedItemsSkipped = 0;
        var channelMetadataMisses = 0;
        var channelsCreated = 0;
        var channelsRefreshed = 0;
        var softDeletedChannelsIgnored = 0;
        var provenanceRowsCreated = 0;
        var duplicateProvenanceRows = 0;
        var failedKeywordReasons = new List<string>();
        var stopRun = false;

        while (!stopRun)
        {
            var foundEligibleKeyword = false;

            foreach (var keyword in keywords)
            {
                var successfulPages = pageCountByKeywordId.GetValueOrDefault(keyword.Id);
                if (failedKeywordIds.Contains(keyword.Id) ||
                    terminalKeywordIds.Contains(keyword.Id) ||
                    (pageLimit > 0 && successfulPages >= pageLimit))
                {
                    continue;
                }

                foundEligibleKeyword = true;
                stateByKeywordId.TryGetValue(keyword.Id, out var state);

                var searchReservation = await dailyQuotaRepository.TryReserveSearchUnitAsync(
                    quotaDate,
                    dailyProcessClaimToken: null,
                    cancellationToken);

                if (searchReservation != QuotaReservationResult.Reserved)
                {
                    RecordKeywordFailure(
                        keyword,
                        QuotaLimitReachedReason,
                        startedAt,
                        ref failedKeywords,
                        failedKeywordReasons);
                    stopRun = true;
                    break;
                }

                var search = await youTubeChannelDiscoveryGateway.SearchAsync(
                    keyword.Keyword,
                    state?.RecentPageToken,
                    cancellationToken);

                if (!search.Succeeded)
                {
                    var failureReason = SanitizeFailureReason(search.FailureReason);
                    if (IsQuotaExceeded(search.FailureReason))
                    {
                        await dailyQuotaRepository.MarkSearchQuotaExhaustedAsync(
                            quotaDate,
                            timeProvider.GetUtcNow(),
                            cancellationToken);
                    }

                    failedKeywordIds.Add(keyword.Id);
                    RecordKeywordFailure(
                        keyword,
                        failureReason,
                        startedAt,
                        ref failedKeywords,
                        failedKeywordReasons);
                    continue;
                }

                searchItemsReceived += search.Items.Count;
                malformedItemsSkipped += search.MalformedItemsSkipped;

                var channelIds = search.Items
                    .Select(item => item.YouTubeChannelId)
                    .Distinct(StringComparer.Ordinal)
                    .ToList();

                var metadataByChannelId = new Dictionary<string, YouTubeChannelMetadata>(StringComparer.Ordinal);
                var metadataBlockedReason = (string?)null;

                foreach (var chunk in channelIds.Chunk(ChannelMetadataChunkSize))
                {
                    var metadataReservation = await dailyQuotaRepository.TryReserveQueriesUnitAsync(
                        quotaDate,
                        dailyProcessClaimToken: null,
                        cancellationToken);

                    if (metadataReservation != QuotaReservationResult.Reserved)
                    {
                        metadataBlockedReason = QuotaLimitReachedReason;
                        break;
                    }

                    var metadata = await youTubeChannelDiscoveryGateway.GetChannelMetadataAsync(
                        chunk,
                        cancellationToken);

                    if (!metadata.Succeeded)
                    {
                        if (IsQuotaExceeded(metadata.FailureReason))
                        {
                            await dailyQuotaRepository.MarkQueriesQuotaExhaustedAsync(
                                quotaDate,
                                timeProvider.GetUtcNow(),
                                cancellationToken);
                        }

                        metadataBlockedReason = SanitizeFailureReason(metadata.FailureReason);
                        break;
                    }

                    foreach (var item in metadata.Items)
                    {
                        metadataByChannelId[item.YouTubeChannelId] = item;
                    }
                }

                if (metadataBlockedReason is not null)
                {
                    failedKeywordIds.Add(keyword.Id);
                    RecordKeywordFailure(
                        keyword,
                        metadataBlockedReason,
                        startedAt,
                        ref failedKeywords,
                        failedKeywordReasons);
                    continue;
                }

                var pageMetadataMisses = 0;
                var candidates = new List<YouTubeChannelCandidate>();
                foreach (var item in search.Items)
                {
                    if (!metadataByChannelId.TryGetValue(item.YouTubeChannelId, out var metadata))
                    {
                        pageMetadataMisses++;
                        continue;
                    }

                    candidates.Add(new YouTubeChannelCandidate(
                        metadata.YouTubeChannelId,
                        metadata.ChannelName,
                        metadata.ChannelThumbnailUrl,
                        metadata.UploadsPlaylistId,
                        item.YouTubeVideoId,
                        item.VideoTitle,
                        item.VideoThumbnailUrl,
                        item.VideoPublishedAt));
                }

                var persistResult = await discoveryRepository.PersistKeywordDiscoveryAsync(
                    keyword,
                    candidates,
                    search.NextPageToken,
                    timeProvider.GetUtcNow(),
                    cancellationToken,
                    quotaDate);

                channelMetadataMisses += pageMetadataMisses;
                channelsCreated += persistResult.ChannelsCreated;
                channelsRefreshed += persistResult.ChannelsRefreshed;
                softDeletedChannelsIgnored += persistResult.SoftDeletedChannelsIgnored;
                provenanceRowsCreated += persistResult.ProvenanceRowsCreated;
                duplicateProvenanceRows += persistResult.DuplicateProvenanceRows;
                successfulKeywordIds.Add(keyword.Id);
                pageCountByKeywordId[keyword.Id] = successfulPages + 1;

                if (state is null)
                {
                    state = new DiscoveryState { KeywordId = keyword.Id };
                    stateByKeywordId[keyword.Id] = state;
                }

                state.RecentPageToken = search.NextPageToken;
                if (search.NextPageToken is null)
                {
                    state.RecentCompletedQuotaDate = quotaDate;
                    terminalKeywordIds.Add(keyword.Id);
                }
            }

            if (!foundEligibleKeyword)
            {
                break;
            }
        }

        var summary = new ChannelDiscoveryRunSummary(
            keywords.Count,
            successfulKeywordIds.Count,
            failedKeywords,
            searchItemsReceived,
            malformedItemsSkipped,
            channelMetadataMisses,
            channelsCreated,
            channelsRefreshed,
            softDeletedChannelsIgnored,
            provenanceRowsCreated,
            duplicateProvenanceRows,
            failedKeywordReasons);

        discoveryActivityLogger.LogRunCompleted(summary, timeProvider.GetUtcNow() - startedAt);

        return summary;
    }

    private void RecordKeywordFailure(
        DiscoveryKeyword keyword,
        string failureReason,
        DateTimeOffset startedAt,
        ref int failedKeywords,
        List<string> failedKeywordReasons)
    {
        failedKeywords++;
        failedKeywordReasons.Add(failureReason);
        discoveryActivityLogger.LogKeywordFailed(
            keyword,
            failureReason,
            timeProvider.GetUtcNow() - startedAt);
    }

    private static bool IsQuotaExceeded(string? failureReason) =>
        string.Equals(failureReason, "QuotaExceeded", StringComparison.Ordinal);

    private static string SanitizeFailureReason(string? failureReason)
    {
        if (string.IsNullOrWhiteSpace(failureReason))
        {
            return "UnknownFailure";
        }

        var sanitized = new string(failureReason
            .Where(character => !char.IsControl(character))
            .ToArray())
            .Trim();

        return sanitized.Length <= 200 ? sanitized : sanitized[..200];
    }
}
