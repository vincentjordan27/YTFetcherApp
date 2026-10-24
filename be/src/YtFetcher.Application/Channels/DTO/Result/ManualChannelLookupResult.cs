namespace YtFetcher.Application.Channels.DTO.Result;

/// <summary>
/// Sanitized outcome of one YouTube handle lookup. Only a <see cref="ManualChannelLookupStatus.Found"/>
/// result carries canonical channel metadata; the other outcomes carry no provider data.
/// </summary>
public sealed record ManualChannelLookupResult(
    ManualChannelLookupStatus Status,
    string? YouTubeChannelId,
    string? ChannelName,
    string? ChannelThumbnailUrl,
    string? UploadsPlaylistId)
{
    public static ManualChannelLookupResult Found(
        string youTubeChannelId,
        string channelName,
        string? channelThumbnailUrl,
        string? uploadsPlaylistId) =>
        new(ManualChannelLookupStatus.Found, youTubeChannelId, channelName, channelThumbnailUrl, uploadsPlaylistId);

    public static ManualChannelLookupResult NotFound() =>
        new(ManualChannelLookupStatus.NotFound, null, null, null, null);

    public static ManualChannelLookupResult Unavailable() =>
        new(ManualChannelLookupStatus.Unavailable, null, null, null, null);
}
