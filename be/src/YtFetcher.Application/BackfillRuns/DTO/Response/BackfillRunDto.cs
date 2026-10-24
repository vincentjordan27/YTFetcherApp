using YtFetcher.Core.BackfillRuns;
using YtFetcher.Core.Channels;

namespace YtFetcher.Application.BackfillRuns.DTO.Response;

/// <summary>
/// One backfill run row with its joined channel data, used both as a list item and as the single-run
/// detail payload. It carries only operator-readable state: a run never exposes a Hangfire job
/// identifier, provider payload, stack trace, credential, or raw exception text.
/// </summary>
public sealed record BackfillRunDto(
    Guid Id,
    Guid ChannelId,
    string ChannelName,
    string YoutubeChannelId,
    string? ChannelThumbnailUrl,
    int PageNumber,
    BackfillTriggerSource TriggerSource,
    BackfillRunStatus Status,
    DateTimeOffset StartedAt,
    DateTimeOffset? CompletedAt,
    string? FailureReason,
    int? FailureCountAtTime,
    /// <summary>
    /// The provider bucket that reached its limit when <see cref="Status"/> is
    /// <see cref="BackfillRunStatus.QUOTA_LIMIT_REACHED"/>; null for every other status.
    /// </summary>
    QuotaLimitBucket? QuotaLimitBucket,
    /// <summary>
    /// True only when the server decides this run may be retried: the run failed, its channel is an active
    /// approved channel whose current page work is terminally failed, and no newer non-deleted run exists for
    /// that channel. It is a response field only and never lives on the Core entity.
    /// </summary>
    bool CanRetry)
{
    /// <summary>
    /// Composes one row from its persisted run and the channel it belongs to. The channel is supplied by
    /// a set-based lookup rather than a per-row query.
    /// </summary>
    public static BackfillRunDto ToDto(ChannelBackfillRun run, Channel channel, bool canRetry) => new(
        run.Id,
        run.ChannelId,
        channel.Name,
        channel.YoutubeChannelId,
        channel.ThumbnailUrl,
        run.PageNumber,
        run.TriggerSource,
        run.Status,
        run.StartedAt,
        run.CompletedAt,
        run.FailureReason,
        run.FailureCountAtTime,
        run.QuotaLimitBucket,
        canRetry);
}
