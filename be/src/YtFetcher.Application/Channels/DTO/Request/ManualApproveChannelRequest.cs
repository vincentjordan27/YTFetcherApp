namespace YtFetcher.Application.Channels.DTO.Request;

/// <summary>
/// Request data for adding one YouTube channel by the operator-supplied handle. The handle is
/// accepted as <c>@handle</c> only; format parsing is Application behavior, not HTTP validation.
/// </summary>
public sealed record ManualApproveChannelRequest(string? ChannelReference);
