using Microsoft.Extensions.Logging;
using YtFetcher.Application.Abstractions.Services;
using YtFetcher.Application.Discovery.DTO.Result;
using YtFetcher.Core.Discovery;

namespace YtFetcher.Infrastructure.Observability;

public sealed class DiscoveryActivityLogger(ILogger<DiscoveryActivityLogger> logger) : IDiscoveryActivityLogger
{
    public void LogRunCompleted(ChannelDiscoveryRunSummary summary, TimeSpan duration) =>
        logger.LogInformation(
            "ChannelDiscoveryRunCompleted {ActiveKeywords} {SuccessfulKeywords} {FailedKeywords} " +
            "{SearchItemsReceived} {MalformedItemsSkipped} {ChannelMetadataMisses} {ChannelsCreated} " +
            "{ChannelsRefreshed} {SoftDeletedChannelsIgnored} {ProvenanceRowsCreated} " +
            "{DuplicateProvenanceRows} {DurationMilliseconds}",
            summary.ActiveKeywords,
            summary.SuccessfulKeywords,
            summary.FailedKeywords,
            summary.SearchItemsReceived,
            summary.MalformedItemsSkipped,
            summary.ChannelMetadataMisses,
            summary.ChannelsCreated,
            summary.ChannelsRefreshed,
            summary.SoftDeletedChannelsIgnored,
            summary.ProvenanceRowsCreated,
            summary.DuplicateProvenanceRows,
            duration.TotalMilliseconds);

    public void LogKeywordFailed(DiscoveryKeyword keyword, string failureReason, TimeSpan duration) =>
        logger.LogWarning(
            "ChannelDiscoveryKeywordFailed {KeywordId} {Keyword} {FailureReason} {DurationMilliseconds}",
            keyword.Id,
            keyword.Keyword,
            failureReason,
            duration.TotalMilliseconds);
}
