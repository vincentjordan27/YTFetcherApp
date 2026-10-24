namespace YtFetcher.Application.Channels.DTO.Request;

/// <summary>Request data for one page of approved channels.</summary>
public sealed record ApprovedChannelsRequest(int Page, int PageSize, string? SearchText);
