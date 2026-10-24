namespace YtFetcher.Application.Backfill.DTO.Result;

/// <summary>
/// Outcome of the pump's dispatch step. <see cref="DispatchedChannelIds"/> are the channels that were
/// moved to <c>SCHEDULING</c> with a reserved quota allocation inside one transaction, in the order the
/// pump must enqueue them.
/// </summary>
public sealed record BackfillDispatchResult(IReadOnlyList<Guid> DispatchedChannelIds);
