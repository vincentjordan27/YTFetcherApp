namespace YtFetcher.Application.DiscoveryKeywords.DTO.Request;

/// <summary>
/// Request data for one page of discovery keywords. Sent as the JSON body of the keyword list action.
/// </summary>
public sealed record DiscoveryKeywordsListRequest(int Page, int PageSize, string? SearchText);
