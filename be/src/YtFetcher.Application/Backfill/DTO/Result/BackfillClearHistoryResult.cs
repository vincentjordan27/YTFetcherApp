namespace YtFetcher.Application.Backfill.DTO.Result;

/// <summary>
/// Outcome of one backfill run retention invocation. <see cref="ReachedBatchLimit"/> is true when the
/// invocation stopped because it had used every allowed batch, which means more expired rows may remain
/// for the next run rather than reporting a silent partial clear.
/// </summary>
public sealed record BackfillClearHistoryResult(
    int DeletedCount,
    int BatchCount,
    bool ReachedBatchLimit);
