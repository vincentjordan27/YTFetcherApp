using Microsoft.EntityFrameworkCore;
using YtFetcher.Application.Abstractions.Repositories;
using YtFetcher.Core.BackfillRuns;
using YtFetcher.Core.Channels;
using YtFetcher.Infrastructure.Persistence;

namespace YtFetcher.Infrastructure.Persistence.Repositories;

/// <summary>
/// PostgreSQL backfill run history. The list read joins the channel table in the database so paging,
/// filtering, ordering, and the joined channel data never become a query per row. Run rows are excluded
/// when they are soft deleted; the joined channel deliberately is not, because deleting or rejecting a
/// channel must never hide its run history.
/// </summary>
public sealed class BackfillRunRepository(YtFetcherDbContext dbContext) : IBackfillRunRepository
{
    public async Task<IReadOnlyList<ChannelBackfillRun>> GetPageAsync(
        int page,
        int pageSize,
        string? searchText,
        BackfillRunStatus? status,
        BackfillTriggerSource? triggerSource,
        DateTimeOffset? startedFromUtc,
        DateTimeOffset? startedToExclusiveUtc,
        CancellationToken cancellationToken) =>
        await FilteredRuns(searchText, status, triggerSource, startedFromUtc, startedToExclusiveUtc)
            .OrderByDescending(run => run.StartedAt)
            .ThenByDescending(run => run.Id)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync(cancellationToken);

    public Task<int> CountAsync(
        string? searchText,
        BackfillRunStatus? status,
        BackfillTriggerSource? triggerSource,
        DateTimeOffset? startedFromUtc,
        DateTimeOffset? startedToExclusiveUtc,
        CancellationToken cancellationToken) =>
        FilteredRuns(searchText, status, triggerSource, startedFromUtc, startedToExclusiveUtc)
            .CountAsync(cancellationToken);

    public async Task<IReadOnlySet<Guid>> GetRetryableRunIdsAsync(
        IReadOnlyCollection<Guid> runIds,
        CancellationToken cancellationToken)
    {
        if (runIds.Count == 0)
        {
            return new HashSet<Guid>();
        }

        // One query, one correlated NOT EXISTS per candidate. The newest run of a channel is the one the
        // ledger lists first, so a run is only retryable when nothing newer exists for that channel, which is
        // what stops an older hidden failure from offering a retry the backend would reject.
        var retryable = await (
                from run in dbContext.ChannelBackfillRuns.AsNoTracking()
                join channel in dbContext.Channels.AsNoTracking() on run.ChannelId equals channel.Id
                where runIds.Contains(run.Id)
                      && !run.IsDeleted
                      && run.StatusValue == nameof(BackfillRunStatus.FAILED)
                      && !channel.IsDeleted
                      && channel.StatusValue == nameof(ChannelStatus.APPROVED)
                      && channel.BackfillStatusValue == nameof(BackfillStatus.FAILED)
                      && !dbContext.ChannelBackfillRuns.Any(newer =>
                          newer.ChannelId == run.ChannelId
                          && !newer.IsDeleted
                          && (newer.StartedAt > run.StartedAt ||
                              (newer.StartedAt == run.StartedAt && newer.Id > run.Id)))
                select run.Id)
            .ToListAsync(cancellationToken);

        return retryable.ToHashSet();
    }

    public Task<ChannelBackfillRun?> GetByIdAsync(Guid runId, CancellationToken cancellationToken) =>
        dbContext.ChannelBackfillRuns
            .AsNoTracking()
            .FirstOrDefaultAsync(run => run.Id == runId && !run.IsDeleted, cancellationToken);

    public async Task<IReadOnlyDictionary<Guid, Channel>> GetChannelsAsync(
        IReadOnlyCollection<Guid> channelIds,
        CancellationToken cancellationToken)
    {
        if (channelIds.Count == 0)
        {
            return new Dictionary<Guid, Channel>();
        }

        var channels = await dbContext.Channels
            .AsNoTracking()
            .Where(channel => channelIds.Contains(channel.Id))
            .ToListAsync(cancellationToken);

        return channels.ToDictionary(channel => channel.Id);
    }

    public async Task<int> DeleteTerminalRunsStartedBeforeAsync(
        DateTimeOffset startedBeforeUtc,
        int batchSize,
        CancellationToken cancellationToken) =>
        // One server-side bulk command: no entity load, no change tracking, no raw SQL. Ordering is the same
        // deterministic oldest-first order retention uses, and the batch stays bounded.
        await dbContext.ChannelBackfillRuns
            .Where(run =>
                run.StatusValue != nameof(BackfillRunStatus.RUNNING) &&
                !run.IsDeleted &&
                run.StartedAt < startedBeforeUtc)
            .OrderBy(run => run.StartedAt)
            .ThenBy(run => run.Id)
            .Take(batchSize)
            .ExecuteDeleteAsync(cancellationToken);

    /// <summary>
    /// Shared filter for the page read and its count so both always describe the same row set.
    /// </summary>
    private IQueryable<ChannelBackfillRun> FilteredRuns(
        string? searchText,
        BackfillRunStatus? status,
        BackfillTriggerSource? triggerSource,
        DateTimeOffset? startedFromUtc,
        DateTimeOffset? startedToExclusiveUtc)
    {
        var runs = dbContext.ChannelBackfillRuns
            .AsNoTracking()
            .Where(run => !run.IsDeleted);

        if (status is not null)
        {
            var statusValue = status.Value.ToString();
            runs = runs.Where(run => run.StatusValue == statusValue);
        }

        if (triggerSource is not null)
        {
            var triggerSourceValue = triggerSource.Value.ToString();
            runs = runs.Where(run => run.TriggerSourceValue == triggerSourceValue);
        }

        if (startedFromUtc is not null)
        {
            runs = runs.Where(run => run.StartedAt >= startedFromUtc.Value);
        }

        if (startedToExclusiveUtc is not null)
        {
            runs = runs.Where(run => run.StartedAt < startedToExclusiveUtc.Value);
        }

        if (!string.IsNullOrEmpty(searchText))
        {
            // Portable LINQ rather than EF.Functions.ILike so the EF InMemory tests stay usable.
            // ToLower() is used instead of ToLowerInvariant() because the Npgsql provider cannot
            // translate the invariant form.
            var search = searchText.ToLower();

            runs =
                from run in runs
                join channel in dbContext.Channels.AsNoTracking() on run.ChannelId equals channel.Id
                where channel.Name.ToLower().Contains(search) ||
                      channel.YoutubeChannelId.ToLower().Contains(search)
                select run;
        }

        return runs;
    }
}
