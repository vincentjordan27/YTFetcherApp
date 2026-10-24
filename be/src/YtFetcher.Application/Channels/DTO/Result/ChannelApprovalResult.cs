using YtFetcher.Core.Channels;

namespace YtFetcher.Application.Channels.DTO.Result;

/// <summary>
/// Outcome of one approval that claims a paired first-page quota slot inside the same transaction as the
/// channel lifecycle mutation. <see cref="Channel"/> is present only for <see cref="ChannelApprovalStatus.Succeeded"/>.
/// </summary>
public sealed record ChannelApprovalResult(ChannelApprovalStatus Status, Channel? Channel)
{
    public static ChannelApprovalResult Succeeded(Channel channel) => new(ChannelApprovalStatus.Succeeded, channel);

    public static ChannelApprovalResult Refused(ChannelApprovalStatus status) => new(status, null);
}
