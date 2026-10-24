namespace YtFetcher.Application.Videos.DTO.Request;

/// <summary>
/// Body of the reference-video list action: page, page size, optional search text, and the sort contract.
/// It is intentionally separate from <see cref="ActiveVideosRequest"/> even though the fields match today.
/// </summary>
public sealed record ReferenceVideosRequest(
    int Page,
    int PageSize,
    string? SearchText,
    string? SortBy,
    string? SortDirection);
