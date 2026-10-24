using YtFetcher.Core.BackfillRuns;
using YtFetcher.Core.Channels;

namespace YtFetcher.Application.Abstractions.Repositories;

/// <summary>
/// Persistence boundary for durable backfill run history. Reads return Core entities or Core-entity
/// dictionaries; the Application use case composes the response. The list read is a join performed by
/// Infrastructure, so the page, its total count, and the joined channel data never cause a query per row.
/// </summary>
public interface IBackfillRunRepository
{
    /// <summary>
    /// Fetches one page of non-deleted runs ordered by <c>started_at DESC, id DESC</c>. Every filter is
    /// optional: <paramref name="searchText"/> matches the channel name or the YouTube channel ID
    /// case-insensitively, the two date bounds filter <c>started_at</c> inclusively as UTC
    /// (<paramref name="startedFromUtc"/> is inclusive and <paramref name="startedToExclusiveUtc"/> is
    /// exclusive so a whole UTC day is covered), and the status and trigger filters match exactly.
    /// </summary>
    Task<IReadOnlyList<ChannelBackfillRun>> GetPageAsync(
        int page,
        int pageSize,
        string? searchText,
        BackfillRunStatus? status,
        BackfillTriggerSource? triggerSource,
        DateTimeOffset? startedFromUtc,
        DateTimeOffset? startedToExclusiveUtc,
        CancellationToken cancellationToken);

    /// <summary>Counts the non-deleted runs that match the same filters as the page read.</summary>
    Task<int> CountAsync(
        string? searchText,
        BackfillRunStatus? status,
        BackfillTriggerSource? triggerSource,
        DateTimeOffset? startedFromUtc,
        DateTimeOffset? startedToExclusiveUtc,
        CancellationToken cancellationToken);

    /// <summary>
    /// Returns the subset of <paramref name="runIds"/> that an operator may retry, decided in one set-based
    /// query: the run is failed and not deleted, its channel is an active approved channel whose current page
    /// work is terminally failed, and no newer non-deleted run exists for that channel. The server is the
    /// authority, so a client can never offer a retry the backend would reject. Returns an empty set
    /// immediately for empty input.
    /// </summary>
    Task<IReadOnlySet<Guid>> GetRetryableRunIdsAsync(
        IReadOnlyCollection<Guid> runIds,
        CancellationToken cancellationToken);

    /// <summary>Fetches one non-deleted run, or <c>null</c> when the run is missing or soft deleted.</summary>
    Task<ChannelBackfillRun?> GetByIdAsync(Guid runId, CancellationToken cancellationToken);

    /// <summary>
    /// Looks up the joined channel rows for a page of runs in one set-based query. Soft-deleted channels
    /// are included on purpose, because deleting or rejecting a channel must never delete or hide its run
    /// history. Returns an empty dictionary immediately for an empty input, so the list read never issues
    /// a query per row.
    /// </summary>
    Task<IReadOnlyDictionary<Guid, Channel>> GetChannelsAsync(
        IReadOnlyCollection<Guid> channelIds,
        CancellationToken cancellationToken);

    /// <summary>
    /// Physically deletes at most <paramref name="batchSize"/> terminal, non-soft-deleted runs whose
    /// <c>started_at</c> is before <paramref name="startedBeforeUtc"/> and returns how many rows were
    /// removed. This is the explicit retention exception to ordinary soft deletion; a run that is still
    /// <c>RUNNING</c> is never removed. The bound keeps one invocation short and lets the caller repeat it.
    /// </summary>
    Task<int> DeleteTerminalRunsStartedBeforeAsync(
        DateTimeOffset startedBeforeUtc,
        int batchSize,
        CancellationToken cancellationToken);
}
