using YtFetcher.Application.Abstractions.Repositories;
using YtFetcher.Application.Abstractions.UseCases;
using YtFetcher.Application.BackfillRuns.DTO.Request;
using YtFetcher.Application.BackfillRuns.DTO.Response;
using YtFetcher.Core.Channels;

namespace YtFetcher.Application.BackfillRuns.UseCases;

/// <summary>
/// Read-only view of the durable backfill run history. It composes the page from one run query, one
/// count query, and one set-based channel lookup, so a page never issues a query per row.
/// </summary>
public sealed class BackfillRunUseCase(IBackfillRunRepository backfillRunRepository) : IBackfillRunUseCase
{
    public async Task<BackfillRunsResponse> GetPageAsync(
        BackfillRunsListRequest request,
        CancellationToken cancellationToken)
    {
        var startedFromUtc = ToUtcDayStart(request.DateFrom);

        // The upper bound is exclusive so an inclusive DateTo covers that whole UTC day.
        var startedToExclusiveUtc = ToUtcDayStart(request.DateTo?.AddDays(1));

        // Normalised once so the page read and its count always describe the same row set.
        var searchText = NormalizeSearchText(request.SearchText);

        var runs = await backfillRunRepository.GetPageAsync(
            request.Page,
            request.PageSize,
            searchText,
            request.Status,
            request.TriggerSource,
            startedFromUtc,
            startedToExclusiveUtc,
            cancellationToken);

        var totalCount = await backfillRunRepository.CountAsync(
            searchText,
            request.Status,
            request.TriggerSource,
            startedFromUtc,
            startedToExclusiveUtc,
            cancellationToken);

        var channels = await backfillRunRepository.GetChannelsAsync(
            runs.Select(run => run.ChannelId).Distinct().ToArray(),
            cancellationToken);

        // Retrieved once for the whole page, never per row.
        var retryableRunIds = await backfillRunRepository.GetRetryableRunIdsAsync(
            runs.Select(run => run.Id).ToArray(),
            cancellationToken);

        return new BackfillRunsResponse(
            runs.Select(run => BackfillRunDto.ToDto(
                run,
                ResolveChannel(channels, run.ChannelId),
                retryableRunIds.Contains(run.Id))).ToArray(),
            request.Page,
            request.PageSize,
            totalCount);
    }

    public async Task<BackfillRunDto?> GetByIdAsync(Guid runId, CancellationToken cancellationToken)
    {
        var run = await backfillRunRepository.GetByIdAsync(runId, cancellationToken);
        if (run is null)
        {
            return null;
        }

        var channels = await backfillRunRepository.GetChannelsAsync([run.ChannelId], cancellationToken);

        // The same operation as the list, for this one run id.
        var retryableRunIds = await backfillRunRepository.GetRetryableRunIdsAsync(
            [run.Id],
            cancellationToken);

        return BackfillRunDto.ToDto(
            run,
            ResolveChannel(channels, run.ChannelId),
            retryableRunIds.Contains(run.Id));
    }

    /// <summary>
    /// The foreign key from a run to its channel guarantees the lookup finds the row. This only describes
    /// the channel as unidentified if that invariant were ever violated, rather than failing the read.
    /// </summary>
    private static Channel ResolveChannel(
        IReadOnlyDictionary<Guid, Channel> channels,
        Guid channelId) =>
        channels.TryGetValue(channelId, out var channel)
            ? channel
            : new Channel { Id = channelId };

    private static string? NormalizeSearchText(string? searchText)
    {
        var trimmed = searchText?.Trim();
        return string.IsNullOrEmpty(trimmed) ? null : trimmed;
    }

    private static DateTimeOffset? ToUtcDayStart(DateOnly? date) =>
        date?.ToDateTime(TimeOnly.MinValue, DateTimeKind.Utc);
}
