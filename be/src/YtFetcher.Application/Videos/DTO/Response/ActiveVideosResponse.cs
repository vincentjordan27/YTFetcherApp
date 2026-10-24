namespace YtFetcher.Application.Videos.DTO.Response;

/// <summary>One server-paged list of active videos.</summary>
public sealed record ActiveVideosResponse(
    IReadOnlyList<ActiveVideoDto> Items,
    int Page,
    int PageSize,
    int TotalCount);
