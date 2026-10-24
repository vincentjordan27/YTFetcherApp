using YtFetcher.Core.Channels;

namespace YtFetcher.Application.Channels.DTO.Result;

/// <summary>
/// Outcome of the manual-add creation transaction, which also claims the paired first-page slot. A duplicate
/// returns the durable existing row; a quota refusal returns no channel and changes nothing.
/// </summary>
public sealed record ManualChannelCreateResult(ChannelApprovalStatus Status, Channel? Channel)
{
    public static ManualChannelCreateResult Created(Channel channel) => new(ChannelApprovalStatus.Succeeded, channel);

    public static ManualChannelCreateResult Duplicate(Channel channel) => new(ChannelApprovalStatus.Duplicate, channel);

    public static ManualChannelCreateResult Refused(ChannelApprovalStatus status) => new(status, null);
}
