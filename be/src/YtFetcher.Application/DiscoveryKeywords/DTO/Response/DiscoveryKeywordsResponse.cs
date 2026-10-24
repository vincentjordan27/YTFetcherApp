namespace YtFetcher.Application.DiscoveryKeywords.DTO.Response;

/// <summary>
/// Response data for one page of discovery keywords. <see cref="ActiveCount"/> is database-wide, so the
/// operator header keeps showing the true active keyword count while a search or page narrows
/// <see cref="Items"/>.
/// </summary>
public sealed record DiscoveryKeywordsResponse(
    IReadOnlyList<DiscoveryKeywordDto> Items,
    int Page,
    int PageSize,
    int TotalCount,
    int ActiveCount);
