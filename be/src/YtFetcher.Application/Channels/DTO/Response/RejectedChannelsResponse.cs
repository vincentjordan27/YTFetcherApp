namespace YtFetcher.Application.Channels.DTO.Response;

/// <summary>Response data for one page of rejected channels.</summary>
public sealed record RejectedChannelsResponse(
    IReadOnlyList<RejectedChannelDto> Items,
    int Page,
    int PageSize,
    int TotalCount);
