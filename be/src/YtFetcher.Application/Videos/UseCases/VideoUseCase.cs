using YtFetcher.Application.Abstractions.Repositories;
using YtFetcher.Application.Abstractions.Services;
using YtFetcher.Application.Abstractions.UseCases;
using YtFetcher.Application.Videos.DTO.Request;
using YtFetcher.Application.Videos.DTO.Response;

namespace YtFetcher.Application.Videos.UseCases;

/// <summary>
/// Reads the paged Active and Reference video lists, resolves their sort contract, and owns the two
/// reference-state mutations. Sorting is described, never applied: the repository owns the single joined
/// query that orders before paging.
/// </summary>
public sealed class VideoUseCase(
    IVideoRepository videoRepository,
    ICurrentActor currentActor,
    TimeProvider timeProvider) : IVideoUseCase
{
    /// <summary>Matches the historical list order and the toolbar wording, so an unqualified request is unchanged.</summary>
    private const VideoListSortBy DefaultSortBy = VideoListSortBy.Views;

    private const VideoListSortDirection DefaultSortDirection = VideoListSortDirection.Desc;

    public async Task<ActiveVideosResponse> GetActiveAsync(
        ActiveVideosRequest request,
        CancellationToken cancellationToken)
    {
        var searchText = NormalizeSearchText(request.SearchText);
        var (sortBy, sortDirection) = ResolveSort(request.SortBy, request.SortDirection);

        // The repository returns Core entities plus the joined channel fields; building the response
        // is an Application concern.
        var rows = await videoRepository.GetActivePageAsync(
            request.Page,
            request.PageSize,
            searchText,
            sortBy,
            sortDirection,
            cancellationToken);

        var totalCount = await videoRepository.CountActiveAsync(searchText, cancellationToken);

        var items = rows
            .Select(row => ActiveVideoDto.ToDto(row.Video, row.ChannelName, row.YoutubeChannelId))
            .ToList();

        return new ActiveVideosResponse(items, request.Page, request.PageSize, totalCount);
    }

    public async Task<ReferenceVideosResponse> GetReferenceAsync(
        ReferenceVideosRequest request,
        CancellationToken cancellationToken)
    {
        var searchText = NormalizeSearchText(request.SearchText);
        var (sortBy, sortDirection) = ResolveSort(request.SortBy, request.SortDirection);

        var rows = await videoRepository.GetReferencePageAsync(
            request.Page,
            request.PageSize,
            searchText,
            sortBy,
            sortDirection,
            cancellationToken);

        var totalCount = await videoRepository.CountReferenceAsync(searchText, cancellationToken);

        var items = rows
            .Select(row => ReferenceVideoDto.ToDto(row.Video, row.ChannelName, row.YoutubeChannelId))
            .ToList();

        return new ReferenceVideosResponse(items, request.Page, request.PageSize, totalCount);
    }

    public async Task<VideoHistoryResponse?> GetHistoryAsync(
        Guid videoId,
        VideoHistoryRequest request,
        CancellationToken cancellationToken)
    {
        var (exists, totalCount, snapshots) = await videoRepository.GetSnapshotHistoryPageAsync(
            videoId,
            request.Page,
            request.PageSize,
            cancellationToken);

        if (!exists)
        {
            return null;
        }

        var items = snapshots.Select(VideoHistoryItemDto.ToDto).ToList();

        return new VideoHistoryResponse(items, request.Page, request.PageSize, totalCount);
    }

    public async Task<ActiveVideoCountResponse> GetActiveCountAsync(CancellationToken cancellationToken) =>
        // The active-list visibility rule is owned by the repository; the count reuses it with no search
        // filter, so the badge can never disagree with the active table.
        new(await videoRepository.CountActiveAsync(null, cancellationToken));

    public Task<bool> MarkAsReferenceAsync(
        MarkVideosReferenceRequest request,
        CancellationToken cancellationToken) =>
        // The all-or-nothing verification and transition live in the repository's short transaction.
        videoRepository.TryMarkAsReferenceAsync(
            request.VideoIds ?? [],
            currentActor.UserId,
            timeProvider.GetUtcNow(),
            cancellationToken);

    public Task<bool> RemoveReferenceAsync(
        UnmarkVideoReferenceRequest request,
        CancellationToken cancellationToken) =>
        // The row lock, the Core mature-stop decision, and the save are one transaction in the repository.
        // No provider call, quota reservation, or Hangfire work is involved.
        videoRepository.TryRemoveReferenceAsync(
            request.VideoId,
            currentActor.UserId,
            timeProvider.GetUtcNow(),
            cancellationToken);

    /// <summary>
    /// Turns the validated wire strings into the shared sort contract. An absent value falls back to the
    /// documented default; the transport validator has already rejected unknown names.
    /// </summary>
    private static (VideoListSortBy SortBy, VideoListSortDirection SortDirection) ResolveSort(
        string? sortBy,
        string? sortDirection) =>
        (
            ParseSortValue(sortBy, DefaultSortBy),
            ParseSortValue(sortDirection, DefaultSortDirection)
        );

    private static TEnum ParseSortValue<TEnum>(string? value, TEnum fallback)
        where TEnum : struct, Enum
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return fallback;
        }

        // Matching the declared names (rather than TryParse alone) also rejects a numeric string, which
        // enum parsing would otherwise accept as an out-of-range or unintended member value.
        return Enum.GetNames<TEnum>().Contains(value, StringComparer.OrdinalIgnoreCase)
            ? Enum.Parse<TEnum>(value, ignoreCase: true)
            : fallback;
    }

    private static string? NormalizeSearchText(string? searchText)
    {
        var trimmed = searchText?.Trim();
        return string.IsNullOrEmpty(trimmed) ? null : trimmed;
    }
}
