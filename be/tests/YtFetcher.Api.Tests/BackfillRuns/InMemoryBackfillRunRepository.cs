using YtFetcher.Application.Abstractions.Repositories;
using YtFetcher.Core.BackfillRuns;
using YtFetcher.Core.Channels;

namespace YtFetcher.Api.Tests.BackfillRuns;

/// <summary>
/// In-memory backfill run store that mirrors the real repository's filtering rules: normal reads exclude
/// soft-deleted runs but include every channel, search matches the channel name or YouTube channel ID
/// case-insensitively, and retention removes at most one batch of terminal rows per call.
/// </summary>
public sealed class InMemoryBackfillRunRepository : IBackfillRunRepository
{
    private readonly List<ChannelBackfillRun> runs = [];
    private readonly Dictionary<Guid, Channel> channels = [];

    public IReadOnlyList<ChannelBackfillRun> All => runs;

    public int RetentionDeleteCallCount { get; private set; }

    public void SeedRun(ChannelBackfillRun run) => runs.Add(run);

    public void SeedChannel(Channel channel) => channels[channel.Id] = channel;

    public Task<IReadOnlyList<ChannelBackfillRun>> GetPageAsync(
        int page,
        int pageSize,
        string? searchText,
        BackfillRunStatus? status,
        BackfillTriggerSource? triggerSource,
        DateTimeOffset? startedFromUtc,
        DateTimeOffset? startedToExclusiveUtc,
        CancellationToken cancellationToken)
    {
        var matches = Filtered(searchText, status, triggerSource, startedFromUtc, startedToExclusiveUtc)
            .OrderByDescending(run => run.StartedAt)
            .ThenByDescending(run => run.Id)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .ToList();

        return Task.FromResult<IReadOnlyList<ChannelBackfillRun>>(matches);
    }

    public Task<int> CountAsync(
        string? searchText,
        BackfillRunStatus? status,
        BackfillTriggerSource? triggerSource,
        DateTimeOffset? startedFromUtc,
        DateTimeOffset? startedToExclusiveUtc,
        CancellationToken cancellationToken) =>
        Task.FromResult(
            Filtered(searchText, status, triggerSource, startedFromUtc, startedToExclusiveUtc).Count());

    public Task<IReadOnlySet<Guid>> GetRetryableRunIdsAsync(
        IReadOnlyCollection<Guid> runIds,
        CancellationToken cancellationToken)
    {
        // Same three conditions as the real query: the run failed, its channel is an active approved channel
        // whose page work is terminally failed, and nothing newer exists for that channel.
        var newestByChannel = runs
            .Where(run => !run.IsDeleted)
            .GroupBy(run => run.ChannelId)
            .ToDictionary(
                group => group.Key,
                group => group.OrderByDescending(run => run.StartedAt).ThenByDescending(run => run.Id).First().Id);

        var retryable = runs
            .Where(run => runIds.Contains(run.Id)
                          && !run.IsDeleted
                          && run.Status == BackfillRunStatus.FAILED
                          && newestByChannel[run.ChannelId] == run.Id
                          && channels.TryGetValue(run.ChannelId, out var channel)
                          && !channel.IsDeleted
                          && channel.Status == ChannelStatus.APPROVED
                          && channel.BackfillStatus == BackfillStatus.FAILED)
            .Select(run => run.Id)
            .ToHashSet();

        return Task.FromResult<IReadOnlySet<Guid>>(retryable);
    }

    public Task<ChannelBackfillRun?> GetByIdAsync(Guid runId, CancellationToken cancellationToken) =>
        Task.FromResult(runs.FirstOrDefault(run => run.Id == runId && !run.IsDeleted));

    public Task<IReadOnlyDictionary<Guid, Channel>> GetChannelsAsync(
        IReadOnlyCollection<Guid> channelIds,
        CancellationToken cancellationToken) =>
        Task.FromResult<IReadOnlyDictionary<Guid, Channel>>(
            channels
                .Where(entry => channelIds.Contains(entry.Key))
                .ToDictionary(entry => entry.Key, entry => entry.Value));

    public Task<int> DeleteTerminalRunsStartedBeforeAsync(
        DateTimeOffset startedBeforeUtc,
        int batchSize,
        CancellationToken cancellationToken)
    {
        RetentionDeleteCallCount++;

        // Materialised first so the fake cannot remove more than one batch in a single call, which is the
        // behavior the use case's batching loop depends on.
        var expired = runs
            .Where(run => run.Status != BackfillRunStatus.RUNNING &&
                          !run.IsDeleted &&
                          run.StartedAt < startedBeforeUtc)
            .OrderBy(run => run.StartedAt)
            .ThenBy(run => run.Id)
            .Take(batchSize)
            .ToArray();

        foreach (var run in expired)
        {
            runs.Remove(run);
        }

        return Task.FromResult(expired.Length);
    }

    private IEnumerable<ChannelBackfillRun> Filtered(
        string? searchText,
        BackfillRunStatus? status,
        BackfillTriggerSource? triggerSource,
        DateTimeOffset? startedFromUtc,
        DateTimeOffset? startedToExclusiveUtc)
    {
        var matches = runs.Where(run => !run.IsDeleted);

        if (status is not null)
        {
            matches = matches.Where(run => run.Status == status.Value);
        }

        if (triggerSource is not null)
        {
            matches = matches.Where(run => run.TriggerSource == triggerSource.Value);
        }

        if (startedFromUtc is not null)
        {
            matches = matches.Where(run => run.StartedAt >= startedFromUtc.Value);
        }

        if (startedToExclusiveUtc is not null)
        {
            matches = matches.Where(run => run.StartedAt < startedToExclusiveUtc.Value);
        }

        if (!string.IsNullOrEmpty(searchText))
        {
            matches = matches.Where(run =>
                channels.TryGetValue(run.ChannelId, out var channel) &&
                (channel.Name.Contains(searchText, StringComparison.OrdinalIgnoreCase) ||
                 channel.YoutubeChannelId.Contains(searchText, StringComparison.OrdinalIgnoreCase)));
        }

        return matches;
    }
}
