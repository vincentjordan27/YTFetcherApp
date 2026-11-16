namespace YtFetcher.Application.Backfill.DTO.Result;

public sealed record BackfillPageExecutionResult(bool ShouldContinue, bool QuotaLimited)
{
    public static readonly BackfillPageExecutionResult Stopped = new(false, false);
    public static readonly BackfillPageExecutionResult Continued = new(true, false);
    public static readonly BackfillPageExecutionResult QuotaStopped = new(false, true);
}
