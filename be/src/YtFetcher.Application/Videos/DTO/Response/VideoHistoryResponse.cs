namespace YtFetcher.Application.Videos.DTO.Response;

/// <summary>One server-paged snapshot history of a single video, newest capture first.</summary>
public sealed record VideoHistoryResponse(
    IReadOnlyList<VideoHistoryItemDto> Items,
    int Page,
    int PageSize,
    int TotalCount);
