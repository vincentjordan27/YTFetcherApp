namespace YtFetcher.Application.Backfill;

/// <summary>
/// Signals the first-page Hangfire adapter that a consecutive execution failure was durably persisted
/// below the failure limit, so the same job must run again after its configured delay. The message is
/// fixed and never contains provider text or secrets.
/// </summary>
public sealed class BackfillRetryRequestedException(string message) : Exception(message);
