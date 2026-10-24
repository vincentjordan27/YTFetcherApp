namespace YtFetcher.Application.Backfill.DTO.Result;

/// <summary>
/// Normalized outcome of one uploads-playlist page. <see cref="Items"/> preserves provider order and
/// may contain malformed entries for the use case to discard.
/// </summary>
public sealed record UploadsPageResult(
    BackfillProviderOutcome Outcome,
    IReadOnlyList<UploadsPageItemResult> Items,
    string? NextPageToken,
    string? FailureReason)
{
    public static UploadsPageResult Succeeded(
        IReadOnlyList<UploadsPageItemResult> items,
        string? nextPageToken) =>
        new(BackfillProviderOutcome.Succeeded, items, nextPageToken, null);

    public static UploadsPageResult QuotaExceeded() =>
        new(BackfillProviderOutcome.QuotaExceeded, [], null, null);

    public static UploadsPageResult NotFound(string failureReason) =>
        new(BackfillProviderOutcome.NotFound, [], null, failureReason);

    public static UploadsPageResult Failed(string failureReason) =>
        new(BackfillProviderOutcome.Failed, [], null, failureReason);
}
