using YtFetcher.Core.Channels;

namespace YtFetcher.Application.Channels.DTO.Result;

/// <summary>Durable result of one atomic selected-channel lifecycle mutation.</summary>
public sealed record ChannelBatchMutationResult(
    ChannelBatchMutationStatus Status,
    IReadOnlyList<Channel> Channels,
    IReadOnlyDictionary<Guid, ChannelStatus> PreviousStatuses)
{
    public static ChannelBatchMutationResult Succeeded(
        IReadOnlyList<Channel> channels,
        IReadOnlyDictionary<Guid, ChannelStatus>? previousStatuses = null) =>
        new(ChannelBatchMutationStatus.Succeeded, channels, previousStatuses ?? new Dictionary<Guid, ChannelStatus>());

    public static ChannelBatchMutationResult Refused(ChannelBatchMutationStatus status) =>
        new(status, [], new Dictionary<Guid, ChannelStatus>());
}
