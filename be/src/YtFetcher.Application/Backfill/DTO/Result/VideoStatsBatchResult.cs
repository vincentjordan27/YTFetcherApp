namespace YtFetcher.Application.Backfill.DTO.Result;

/// <summary>
/// Normalized outcome of one batch statistics call. A successful response with no items is valid:
/// the page was processed even though YouTube returned nothing.
/// </summary>
public sealed record VideoStatsBatchResult(
    BackfillProviderOutcome Outcome,
    IReadOnlyList<VideoStatsItemResult> Items,
    string? FailureReason)
{
    public static VideoStatsBatchResult Succeeded(IReadOnlyList<VideoStatsItemResult> items) =>
        new(BackfillProviderOutcome.Succeeded, items, null);

    public static VideoStatsBatchResult QuotaExceeded() =>
        new(BackfillProviderOutcome.QuotaExceeded, [], null);

    public static VideoStatsBatchResult NotFound(string failureReason) =>
        new(BackfillProviderOutcome.NotFound, [], failureReason);

    public static VideoStatsBatchResult Failed(string failureReason) =>
        new(BackfillProviderOutcome.Failed, [], failureReason);
}
