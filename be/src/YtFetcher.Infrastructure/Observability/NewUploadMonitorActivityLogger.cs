using Microsoft.Extensions.Logging;
using YtFetcher.Application.Abstractions.Services;

namespace YtFetcher.Infrastructure.Observability;

/// <summary>
/// Structured, secret-free operator events for M6 new-upload monitoring. Only a provider or persistence
/// failure is an event; a normal successful channel check is durable state plus the daily completion summary.
/// </summary>
public sealed class NewUploadMonitorActivityLogger(ILogger<NewUploadMonitorActivityLogger> logger)
    : INewUploadMonitorActivityLogger
{
    public void LogProviderFailed(Guid channelId, string operation, string reason) =>
        logger.LogWarning(
            "NewUploadMonitorProviderFailed {ChannelId} {Operation} {Reason}",
            channelId,
            operation,
            reason);

    public void LogPersistenceFailed(Guid channelId, string operation, Exception exception) =>
        logger.LogError(
            exception,
            "NewUploadMonitorPersistenceFailed {ChannelId} {Operation}",
            channelId,
            operation);
}
