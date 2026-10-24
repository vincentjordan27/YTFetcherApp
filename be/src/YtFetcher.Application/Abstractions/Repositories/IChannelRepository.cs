using YtFetcher.Application.Channels.DTO.Result;
using YtFetcher.Core.Channels;
using YtFetcher.Core.Discovery;

namespace YtFetcher.Application.Abstractions.Repositories;

/// <summary>
/// Persistence boundary for channel reads and mutation persistence. Implemented in Infrastructure.
/// Every read returns Core entities or scalars; the Application use case maps them to response data.
/// </summary>
public interface IChannelRepository
{
    /// <summary>Fetches a non-deleted channel without tracking it, for read-only use cases.</summary>
    Task<Channel?> GetActiveAsync(Guid channelId, CancellationToken cancellationToken);

    /// <summary>Fetches a non-deleted tracked channel so a domain mutation can be persisted.</summary>
    Task<Channel?> GetActiveForUpdateAsync(Guid channelId, CancellationToken cancellationToken);

    /// <summary>
    /// Fetches one page of non-deleted pending channels, newest first, applying the query's page,
    /// page size, and optional search text.
    /// </summary>
    Task<IReadOnlyList<Channel>> GetPendingPageAsync(
        int page,
        int pageSize,
        string? searchText,
        CancellationToken cancellationToken);

    /// <summary>Counts the non-deleted pending channels that match the optional search text.</summary>
    Task<int> CountPendingAsync(string? searchText, CancellationToken cancellationToken);

    /// <summary>
    /// Fetches one page of non-deleted approved or rejected channels, newest first for that status,
    /// ordered by the status's own lifecycle timestamp. Only those two statuses are accepted.
    /// </summary>
    Task<IReadOnlyList<Channel>> GetReviewPageAsync(
        ChannelStatus status,
        int page,
        int pageSize,
        string? searchText,
        CancellationToken cancellationToken);

    /// <summary>Counts the non-deleted approved or rejected channels that match the optional search text.</summary>
    Task<int> CountReviewChannelsAsync(
        ChannelStatus status,
        string? searchText,
        CancellationToken cancellationToken);

    /// <summary>
    /// Counts every non-deleted channel grouped by lifecycle status in one query. Statuses without
    /// rows are absent from the result.
    /// </summary>
    Task<IReadOnlyDictionary<ChannelStatus, int>> CountChannelsByStatusAsync(CancellationToken cancellationToken);

    /// <summary>
    /// Counts the non-deleted discovery evidence rows for the given channels in one grouped query.
    /// Channels without evidence are absent from the result.
    /// </summary>
    Task<IReadOnlyDictionary<Guid, int>> CountEvidenceAsync(
        IReadOnlyCollection<Guid> channelIds,
        CancellationToken cancellationToken);

    /// <summary>
    /// Fetches the newest non-deleted discovery evidence entities for one channel, capped at
    /// <paramref name="limit"/>. Returns an empty list when the channel has no evidence.
    /// </summary>
    Task<IReadOnlyList<ChannelDiscovery>> GetEvidenceAsync(
        Guid channelId,
        int limit,
        CancellationToken cancellationToken);

    /// <summary>
    /// Fetches active, non-deleted discovery keywords by id in one set-based query. Returns an empty
    /// dictionary immediately for an empty input.
    /// </summary>
    Task<IReadOnlyDictionary<Guid, DiscoveryKeyword>> GetDiscoveryKeywordsByIdsAsync(
        IReadOnlyCollection<Guid> keywordIds,
        CancellationToken cancellationToken);

    /// <summary>Persists changes already made to tracked channel entities.</summary>
    Task SaveChangesAsync(CancellationToken cancellationToken);

    /// <summary>
    /// Inserts <paramref name="channel"/> (already built by the domain as APPROVED + SCHEDULING) in one short
    /// transaction, or returns the durable existing row for the canonical YouTube channel ID. The same
    /// transaction first claims the paired first-page Quota + Video Batch Stats slot for
    /// <paramref name="quotaDate"/> and sets the channel's <c>first_page_quota_date</c>. Returns the quota
    /// refusal status without touching any state when the window is not ready or the daily maximum is reached.
    /// Two simultaneous adds are safe: the unique YouTube channel ID constraint lets exactly one insert commit
    /// and the loser re-reads the durable row. It never calls YouTube or Hangfire.
    /// </summary>
    Task<ManualChannelCreateResult> CreateApprovedForSchedulingAsync(
        Channel channel,
        DateOnly quotaDate,
        CancellationToken cancellationToken);

    /// <summary>
    /// Opens a short transaction, locks the active row with <c>FOR UPDATE</c>, claims the paired first-page
    /// Quota + Video Batch Stats slot for <paramref name="quotaDate"/>, and applies the entity's approval plus
    /// scheduling-start transition in one commit. Returns the quota refusal status without changing any state
    /// when the window is not ready or the daily approved-channel maximum is reached.
    /// </summary>
    Task<ChannelApprovalResult> TryApproveForSchedulingAsync(
        Guid channelId,
        DateOnly quotaDate,
        Guid approvedByUserId,
        DateTimeOffset utcNow,
        CancellationToken cancellationToken);

    /// <summary>
    /// Atomically validates every selected Pending row, reserves every required first-page slot, and moves
    /// all selected channels to APPROVED + SCHEDULING. A missing row, lifecycle conflict, or insufficient
    /// quota refuses the complete selection without changing a channel or ledger counter.
    /// </summary>
    Task<ChannelBatchMutationResult> TryApproveManyForSchedulingAsync(
        IReadOnlyList<Guid> channelIds,
        DateOnly quotaDate,
        Guid approvedByUserId,
        DateTimeOffset utcNow,
        CancellationToken cancellationToken);

    /// <summary>
    /// Atomically rejects every selected active Pending or Approved row with one shared reason. Any missing or
    /// no-longer-rejectable row refuses the complete selection without changing another row.
    /// </summary>
    Task<ChannelBatchMutationResult> TryRejectManyAsync(
        IReadOnlyList<Guid> channelIds,
        Guid rejectedByUserId,
        string reason,
        DateTimeOffset utcNow,
        CancellationToken cancellationToken);

    /// <summary>
    /// Locks the active row and performs only <c>SCHEDULING -&gt; SCHEDULING_FAILED</c> with the exact
    /// safe reason. Returns the resulting channel, or <c>null</c> when the expected scheduling state
    /// is gone.
    /// </summary>
    Task<Channel?> TryFailSchedulingAsync(
        Guid channelId,
        string safeReason,
        DateTimeOffset utcNow,
        CancellationToken cancellationToken);

}
