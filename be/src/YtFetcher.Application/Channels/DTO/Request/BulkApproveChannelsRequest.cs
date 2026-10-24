namespace YtFetcher.Application.Channels.DTO.Request;

/// <summary>Body of the all-or-nothing Pending-channel approval action.</summary>
public sealed record BulkApproveChannelsRequest(IReadOnlyList<Guid>? ChannelIds);
