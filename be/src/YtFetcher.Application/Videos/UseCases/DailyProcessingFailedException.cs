namespace YtFetcher.Application.Videos.UseCases;

/// <summary>
/// Signals that the statistics phase hit a provider failure that is not a quota outcome. The run must not
/// continue to quota finalization, gate opening, or backfill dispatch, and Hangfire must mark it failed, so
/// this is thrown rather than returned. The message is built from safe scalars only.
/// </summary>
public sealed class DailyProcessingFailedException(string failureReason)
    : Exception($"The daily statistics phase failed: {failureReason}.")
{
    /// <summary>Safe provider failure reason, or a fixed fallback when the provider gave none.</summary>
    public string FailureReason { get; } = failureReason;
}
