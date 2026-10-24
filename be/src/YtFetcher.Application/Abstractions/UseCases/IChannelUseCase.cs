using YtFetcher.Application.Channels.DTO.Request;
using YtFetcher.Application.Channels.DTO.Result;
using YtFetcher.Application.Channels.DTO.Response;

namespace YtFetcher.Application.Abstractions.UseCases;

/// <summary>
/// Application use-case boundary for reading channel candidates, the approved and rejected review
/// ledgers, the navigation counts, and the review lifecycle.
/// </summary>
public interface IChannelUseCase
{
    Task<PendingChannelsResponse> GetPendingAsync(
        PendingChannelsRequest request,
        CancellationToken cancellationToken);

    Task<ApprovedChannelsResponse> GetApprovedChannelsAsync(
        ApprovedChannelsRequest request,
        CancellationToken cancellationToken);

    Task<RejectedChannelsResponse> GetRejectedChannelsAsync(
        RejectedChannelsRequest request,
        CancellationToken cancellationToken);

    /// <summary>Current non-deleted counts for the three review ledgers, used by the tab badges.</summary>
    Task<ReviewCountsResponse> GetReviewCountsAsync(CancellationToken cancellationToken);

    /// <summary>
    /// Returns the latest evidence rows for an active channel, or <c>null</c> when the channel is
    /// missing or soft deleted so the transport can answer <c>404</c>.
    /// </summary>
    Task<ChannelEvidenceResponse?> GetEvidenceAsync(Guid channelId, CancellationToken cancellationToken);

    Task<ChannelDetailDto?> GetByIdAsync(Guid channelId, CancellationToken cancellationToken);

    Task<ChannelMutationResponse> ApproveAsync(
        Guid channelId,
        ApproveChannelRequest request,
        CancellationToken cancellationToken);

    Task<ChannelBatchMutationStatus> ApproveManyAsync(
        BulkApproveChannelsRequest request,
        CancellationToken cancellationToken);

    /// <summary>
    /// Adds one YouTube channel identified by an operator-supplied <c>@handle</c>: validates the
    /// reference, looks it up once through <see cref="Abstractions.Services.IYouTubeChannelLookupGateway"/>,
    /// persists it as APPROVED + SCHEDULING, then schedules the existing first-page backfill path.
    /// Existing rows are never revived or overwritten.
    /// </summary>
    Task<ChannelMutationResponse> ManualApproveAsync(
        ManualApproveChannelRequest request,
        CancellationToken cancellationToken);

    /// <summary>
    /// Operator retry of a terminally failed backfill page. Valid only for an active, approved channel
    /// whose backfill status is <c>FAILED</c>. It clears only the execution-failure metadata and returns
    /// the channel to the ordinary dispatch path; it never enqueues work, reserves quota, moves the
    /// durable cursor, resets the committed page count, or refreezes the cutoff. A later database change
    /// is answered as a conflict carrying the row actually read.
    /// </summary>
    Task<ChannelMutationResponse> RetryBackfillAsync(
        Guid channelId,
        RetryBackfillRequest request,
        CancellationToken cancellationToken);

    /// <summary>Returns a rejected channel to Pending review without scheduling or spending quota.</summary>
    Task<ChannelMutationResponse> ReopenAsync(
        Guid channelId,
        ReopenChannelRequest request,
        CancellationToken cancellationToken);

    Task<ChannelMutationResponse> RejectAsync(
        Guid channelId,
        RejectChannelRequest request,
        CancellationToken cancellationToken);

    Task<ChannelBatchMutationStatus> RejectManyAsync(
        BulkRejectChannelsRequest request,
        CancellationToken cancellationToken);

    Task<ChannelMutationResponse> SoftDeleteAsync(
        Guid channelId,
        DeleteChannelRequest request,
        CancellationToken cancellationToken);
}
