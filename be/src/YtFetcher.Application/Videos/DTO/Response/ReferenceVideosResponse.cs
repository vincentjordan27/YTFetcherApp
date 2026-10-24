namespace YtFetcher.Application.Videos.DTO.Response;

/// <summary>One server-paged list of reference videos.</summary>
public sealed record ReferenceVideosResponse(
    IReadOnlyList<ReferenceVideoDto> Items,
    int Page,
    int PageSize,
    int TotalCount);
