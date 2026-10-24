namespace YtFetcher.Application.Backfill.DTO.Result;

/// <summary>
/// Normalized outcome of one YouTube backfill provider call. <see cref="QuotaExceeded"/> follows the
/// quota-exhaustion behavior; every other non-success outcome records one safe backfill failure.
/// </summary>
public enum BackfillProviderOutcome
{
    Succeeded,
    QuotaExceeded,
    NotFound,
    Failed
}
