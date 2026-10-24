namespace YtFetcher.Application.Videos.DTO.Request;

/// <summary>
/// Body of the active-video list action: page, page size, optional search text, and the sort contract.
/// Sort values are validated at the transport boundary; a missing value falls back to views descending.
/// </summary>
public sealed record ActiveVideosRequest(
    int Page,
    int PageSize,
    string? SearchText,
    string? SortBy,
    string? SortDirection);
