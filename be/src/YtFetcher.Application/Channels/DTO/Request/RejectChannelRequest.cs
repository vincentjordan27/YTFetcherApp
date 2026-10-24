namespace YtFetcher.Application.Channels.DTO.Request;

/// <summary>Request data for rejecting a channel.</summary>
public sealed record RejectChannelRequest(string? Reason);
