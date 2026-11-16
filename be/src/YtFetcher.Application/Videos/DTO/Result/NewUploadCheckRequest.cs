namespace YtFetcher.Application.Videos.DTO.Result;

/// <summary>
/// Input of one per-channel new-upload persistence transaction: the owned Pacific date and claim token, the
/// monitoring-phase instant that becomes the channel's success marker, the playlist id the page confirmed,
/// and the short-form candidates that still need an insert decision.
/// </summary>
public sealed record NewUploadCheckRequest(
    DateOnly QuotaDate,
    Guid ChannelId,
    Guid DailyProcessClaimToken,
    DateTimeOffset PhaseStartedAtUtc,
    string? UploadsPlaylistId,
    IReadOnlyList<NewUploadCheckItem> Items);
