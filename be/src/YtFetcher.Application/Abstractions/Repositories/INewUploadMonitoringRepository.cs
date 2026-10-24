using YtFetcher.Application.Videos.DTO.Result;
using YtFetcher.Core.Channels;

namespace YtFetcher.Application.Abstractions.Repositories;

/// <summary>
/// Persistence boundary for M6 new-upload monitoring. The candidate read returns Core channels only; the
/// per-channel write re-reads the daily owner, the active channel, and the known video ids inside one short
/// Infrastructure transaction, so a page that raced with another flow can never insert a duplicate video or
/// a second initial snapshot.
/// </summary>
public interface INewUploadMonitoringRepository
{
    /// <summary>
    /// Returns the non-deleted <c>APPROVED</c> channels whose backfill is <c>COMPLETED</c> and whose
    /// <c>last_upload_check_at</c> is null or before <paramref name="quotaDateStartUtc"/>. The order is
    /// deterministic: a null marker first, then the oldest marker, then id ascending.
    /// </summary>
    Task<IReadOnlyList<Channel>> GetUploadMonitorCandidatesAsync(
        DateTimeOffset quotaDateStartUtc,
        CancellationToken cancellationToken);

    /// <summary>
    /// Returns the subset of <paramref name="youtubeVideoIds"/> that already exist in <c>videos</c>. Soft-deleted
    /// rows count as known, exactly like M5, so a deleted video is never revived or given a second snapshot.
    /// </summary>
    Task<IReadOnlySet<string>> GetKnownYoutubeVideoIdsAsync(
        IReadOnlyCollection<string> youtubeVideoIds,
        CancellationToken cancellationToken);

    /// <summary>
    /// Persists one fully processed channel page in a single transaction: inserts only unknown short videos
    /// with exactly one initial snapshot each, stores the uploads playlist id when it was resolved during this
    /// check, and advances the successful-check marker to the phase start. It never advances the marker when
    /// the insert or the marker write cannot commit.
    /// </summary>
    Task<NewUploadCheckPersistResult> PersistUploadCheckAsync(
        NewUploadCheckRequest request,
        CancellationToken cancellationToken);
}
