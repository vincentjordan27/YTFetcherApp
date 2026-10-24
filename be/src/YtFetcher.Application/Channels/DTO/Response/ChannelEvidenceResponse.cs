namespace YtFetcher.Application.Channels.DTO.Response;

/// <summary>Response data for the evidence action: the newest rows for one channel.</summary>
public sealed record ChannelEvidenceResponse(IReadOnlyList<ChannelEvidenceDto> Items);
