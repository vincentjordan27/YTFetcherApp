using YtFetcher.Application.Videos.DTO.Request;
using YtFetcher.Application.Videos.DTO.Response;

namespace YtFetcher.Application.Abstractions.UseCases;

/// <summary>Application use-case boundary for reading tracked videos and changing their reference state.</summary>
public interface IVideoUseCase
{
    Task<ActiveVideosResponse> GetActiveAsync(
        ActiveVideosRequest request,
        CancellationToken cancellationToken);

    Task<ReferenceVideosResponse> GetReferenceAsync(
        ReferenceVideosRequest request,
        CancellationToken cancellationToken);

    /// <summary>
    /// Reads one page of a single video's snapshot history on demand. Returns <c>null</c> when the non-deleted
    /// video does not exist, which the transport maps to <c>404</c>.
    /// </summary>
    Task<VideoHistoryResponse?> GetHistoryAsync(
        Guid videoId,
        VideoHistoryRequest request,
        CancellationToken cancellationToken);

    /// <summary>Counts every active tracked video, matching the active-list visibility rule.</summary>
    Task<ActiveVideoCountResponse> GetActiveCountAsync(CancellationToken cancellationToken);

    /// <summary>
    /// Marks every requested video reference in one all-or-nothing operation. Returns <c>false</c>
    /// when any requested id is missing, soft deleted, already reference, or not active; in that case
    /// no row was changed.
    /// </summary>
    Task<bool> MarkAsReferenceAsync(
        MarkVideosReferenceRequest request,
        CancellationToken cancellationToken);

    /// <summary>
    /// Removes one reference video from Reference. Returns <c>false</c> when the row is missing, soft
    /// deleted, or no longer a reference video; in that case no row was changed. The tracking status is
    /// re-decided by the Core mature-stop rule, and no provider work or quota is involved.
    /// </summary>
    Task<bool> RemoveReferenceAsync(
        UnmarkVideoReferenceRequest request,
        CancellationToken cancellationToken);
}
