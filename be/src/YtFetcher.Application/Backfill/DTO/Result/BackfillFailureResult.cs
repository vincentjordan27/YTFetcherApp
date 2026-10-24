using YtFetcher.Core.Channels;

namespace YtFetcher.Application.Backfill.DTO.Result;

/// <summary>Outcome of one committed failure-state transaction.</summary>
public sealed record BackfillFailureResult(
    int FailureCount,
    bool ReachedFailureLimit,
    BackfillStatus BackfillStatus,
    bool ShouldRequestRetry = true);
