namespace YtFetcher.Application.Channels.DTO.Response;

/// <summary>Application result of a channel lifecycle mutation, interpreted by the transport.</summary>
public sealed record ChannelMutationResponse(
    ChannelMutationStatus Status,
    ChannelDetailDto? Channel,
    string? CurrentStatus,
    string? ValidationMessage,
    string? CurrentBackfillStatus = null);
