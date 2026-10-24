namespace YtFetcher.Application.Abstractions.Services;

/// <summary>
/// Observability boundary for M6 new-upload monitoring. It carries only the failure events the operator needs:
/// a provider or persistence failure with the channel id, a safe operation name, and a non-technical reason.
/// A normal successful check is durable state plus the daily completion event, not an event.
/// </summary>
public interface INewUploadMonitorActivityLogger
{
    /// <summary>One provider call for a channel failed. The reason is fixed, readable text, never raw provider output.</summary>
    void LogProviderFailed(Guid channelId, string operation, string reason);

    /// <summary>
    /// The per-channel persistence transaction failed. The exception is carried for the operator diagnostic;
    /// the channel marker is left unchanged so a later same-date retry revisits this channel.
    /// </summary>
    void LogPersistenceFailed(Guid channelId, string operation, Exception exception);
}
