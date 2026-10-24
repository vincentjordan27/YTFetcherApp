namespace YtFetcher.Application.Channels.DTO.Response;

/// <summary>Response data for one page of pending channels.</summary>
public sealed record PendingChannelsResponse(
    IReadOnlyList<PendingChannelDto> Items,
    int Page,
    int PageSize,
    int TotalCount);
