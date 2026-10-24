namespace YtFetcher.Application.Channels.DTO.Request;

/// <summary>
/// Request data for approving a channel. It carries no value today; it exists so every non-GET
/// channel action accepts a body and the use-case signature stays stable when fields are added.
/// </summary>
public sealed record ApproveChannelRequest;
