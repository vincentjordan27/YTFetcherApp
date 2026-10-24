namespace YtFetcher.Application.Discovery.DTO.Result;

/// <summary>
/// Sanitized result of one channel-discovery run. Contains counts and failure categories only.
/// </summary>
public sealed record ChannelDiscoveryRunSummary(
    int ActiveKeywords,
    int SuccessfulKeywords,
    int FailedKeywords,
    int SearchItemsReceived,
    int MalformedItemsSkipped,
    int ChannelMetadataMisses,
    int ChannelsCreated,
    int ChannelsRefreshed,
    int SoftDeletedChannelsIgnored,
    int ProvenanceRowsCreated,
    int DuplicateProvenanceRows,
    IReadOnlyList<string> FailedKeywordReasons);
