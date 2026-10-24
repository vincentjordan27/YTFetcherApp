namespace YtFetcher.Application.Channels.DTO.Request;

/// <summary>Request data for one page of rejected channels.</summary>
public sealed record RejectedChannelsRequest(int Page, int PageSize, string? SearchText);
