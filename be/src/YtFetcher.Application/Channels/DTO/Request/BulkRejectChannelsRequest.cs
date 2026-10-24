namespace YtFetcher.Application.Channels.DTO.Request;

/// <summary>Body of the all-or-nothing Pending or Approved channel rejection action.</summary>
public sealed record BulkRejectChannelsRequest(IReadOnlyList<Guid>? ChannelIds, string? Reason);
