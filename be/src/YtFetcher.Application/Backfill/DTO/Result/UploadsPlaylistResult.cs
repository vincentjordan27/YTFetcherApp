namespace YtFetcher.Application.Backfill.DTO.Result;

/// <summary>
/// Normalized outcome of resolving a channel's uploads playlist. A successful value always carries a
/// non-blank playlist id; every non-success value carries a safe operator-facing reason.
/// </summary>
public sealed record UploadsPlaylistResult(
    BackfillProviderOutcome Outcome,
    string? UploadsPlaylistId,
    string? FailureReason)
{
    public static UploadsPlaylistResult Succeeded(string uploadsPlaylistId) =>
        new(BackfillProviderOutcome.Succeeded, uploadsPlaylistId, null);

    public static UploadsPlaylistResult QuotaExceeded() =>
        new(BackfillProviderOutcome.QuotaExceeded, null, null);

    public static UploadsPlaylistResult NotFound(string failureReason) =>
        new(BackfillProviderOutcome.NotFound, null, failureReason);

    public static UploadsPlaylistResult Failed(string failureReason) =>
        new(BackfillProviderOutcome.Failed, null, failureReason);
}
