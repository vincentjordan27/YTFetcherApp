namespace YtFetcher.Application.Channels.DTO.Request;

/// <summary>
/// Request data for one page of pending channels. Sent as the JSON body of the pending list action.
/// </summary>
public sealed record PendingChannelsRequest(int Page, int PageSize, string? SearchText);
