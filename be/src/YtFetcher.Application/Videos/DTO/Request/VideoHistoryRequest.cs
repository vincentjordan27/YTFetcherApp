namespace YtFetcher.Application.Videos.DTO.Request;

/// <summary>Body of the on-demand per-video snapshot history action: page and page size only.</summary>
public sealed record VideoHistoryRequest(int Page, int PageSize);
