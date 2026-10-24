namespace YtFetcher.Application.Quotas.DTO.Result;

/// <summary>
/// Outcome of the one-way daily finalization. <see cref="GateOpened"/> is false when the date was not owned,
/// or when either required backfill bucket has no usable remainder after the protected first-page slots.
/// </summary>
public sealed record DailyFinalizationResult(
    bool Finalized,
    bool GateOpened,
    int QueriesRemainder,
    int VideoBatchStatsRemainder);
