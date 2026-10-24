namespace YtFetcher.Application.Channels.DTO.Response;

/// <summary>Response data for one page of approved channels.</summary>
public sealed record ApprovedChannelsResponse(
    IReadOnlyList<ApprovedChannelDto> Items,
    int Page,
    int PageSize,
    int TotalCount);
