namespace YtFetcher.Application.Backfill.DTO.Result;

/// <summary>
/// Outcome of the pump's recovery step. <see cref="RecoveredChannelIds"/> are the channels whose queue
/// scheduling was restarted and which therefore need one enqueue each with the recovery trigger.
/// <see cref="AbandonedRunCount"/> counts the stale running attempts that were closed without a result;
/// those channels re-enter the ordinary candidate path instead of being enqueued directly.
/// </summary>
public sealed record BackfillRecoveryResult(
    IReadOnlyList<Guid> RecoveredChannelIds,
    int AbandonedRunCount);
