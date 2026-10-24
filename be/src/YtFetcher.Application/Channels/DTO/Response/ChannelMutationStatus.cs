namespace YtFetcher.Application.Channels.DTO.Response;

/// <summary>Status returned by a channel lifecycle mutation.</summary>
public enum ChannelMutationStatus
{
    Succeeded,
    NotFound,
    Conflict,
    ValidationError,

    /// <summary>The external provider could not be reached, so the requested check is unavailable.</summary>
    ProviderUnavailable,

    /// <summary>The daily approved-channel maximum or the ordinary request quota is reached for today (HTTP 429).</summary>
    QuotaExceededToday,

    /// <summary>Phase 0 has not initialized today's first-page window yet (HTTP 503).</summary>
    QuotaWindowNotReady
}
