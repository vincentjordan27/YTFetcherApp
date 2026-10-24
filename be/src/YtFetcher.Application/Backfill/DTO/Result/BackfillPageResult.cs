namespace YtFetcher.Application.Backfill.DTO.Result;

/// <summary>
/// Input of the one page-persistence transaction. The use case builds it from the two gateway results;
/// <see cref="UploadItems"/> keeps provider order without duplicates and <see cref="StatsItems"/> holds
/// the statistics matched back to those items. <see cref="IsChannelComplete"/> is the use case's page
/// decision: true when the page has no next token, walked past the frozen cutoff, or reached a positive
/// page limit.
/// </summary>
public sealed record BackfillPageResult(
    Guid ChannelId,
    Guid ClaimToken,
    string UploadsPlaylistId,
    IReadOnlyList<UploadsPageItemResult> UploadItems,
    IReadOnlyList<VideoStatsItemResult> StatsItems,
    string? NextPageToken,
    bool IsChannelComplete,
    DateTimeOffset RunStartedAt);
