using Microsoft.Extensions.Logging;
using YtFetcher.Application.Abstractions.Services;

namespace YtFetcher.Infrastructure.Observability;

/// <summary>
/// Structured, secret-free operator events for the backfill. Failures and degradations are logged; normal
/// successful page and queue control flow is not, because the durable rows and the daily job summary already
/// record it.
/// </summary>
public sealed class BackfillActivityLogger(ILogger<BackfillActivityLogger> logger) : IBackfillActivityLogger
{
    public void LogFirstPageProviderFailed(Guid channelId, string operation, string reason) =>
        logger.LogWarning(
            "ChannelBackfillFirstPageProviderFailed {ChannelId} {Operation} {Reason}",
            channelId,
            operation,
            reason);

    public void LogFirstPageFailed(Guid channelId, int failureCount, int failureLimit, string reason) =>
        logger.LogWarning(
            "ChannelBackfillFirstPageFailed {ChannelId} {FailureCount} {FailureLimit} {Reason}",
            channelId,
            failureCount,
            failureLimit,
            reason);

    public void LogFirstPageQuotaExceeded(Guid channelId, DateOnly quotaDate, string operation) =>
        logger.LogWarning(
            "ChannelBackfillFirstPageQuotaExceeded {ChannelId} {QuotaDate} {Operation}",
            channelId,
            quotaDate,
            operation);

    public void LogFailureStateWriteFailed(Guid channelId, string operation, Exception exception) =>
        logger.LogError(
            exception,
            "ChannelBackfillFailureStateWriteFailed {ChannelId} {Operation}",
            channelId,
            operation);

    public void LogPersistenceFailed(Guid channelId, string operation, Exception exception) =>
        logger.LogError(
            exception,
            "ChannelBackfillFirstPagePersistenceFailed {ChannelId} {Operation}",
            channelId,
            operation);

    public void LogSchedulingFailed(Guid channelId, Exception exception) =>
        logger.LogError(exception, "ChannelBackfillSchedulingFailed {ChannelId}", channelId);

    public void LogSchedulingStateWriteFailed(Guid channelId, Exception exception) =>
        logger.LogError(exception, "ChannelBackfillSchedulingStateWriteFailed {ChannelId}", channelId);

    public void LogTerminalPageFailure(Guid channelId, int failureCount, string reason) =>
        logger.LogWarning(
            "ChannelBackfillTerminalPageFailure {ChannelId} {FailureCount} {Reason}",
            channelId,
            failureCount,
            reason);

    public void LogRetentionCleared(int deletedCount, int batchCount) =>
        logger.LogInformation(
            "ChannelBackfillRetentionCleared {DeletedCount} {BatchCount}",
            deletedCount,
            batchCount);
}
