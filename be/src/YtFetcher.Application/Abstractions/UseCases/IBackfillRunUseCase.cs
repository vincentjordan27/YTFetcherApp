using YtFetcher.Application.BackfillRuns.DTO.Request;
using YtFetcher.Application.BackfillRuns.DTO.Response;

namespace YtFetcher.Application.Abstractions.UseCases;

/// <summary>Application use-case boundary for reading durable backfill run history.</summary>
public interface IBackfillRunUseCase
{
    /// <summary>
    /// Reads one page of run history. The transport validator owns page, page size, and date-range
    /// validation, so this reads the validated request as given.
    /// </summary>
    Task<BackfillRunsResponse> GetPageAsync(
        BackfillRunsListRequest request,
        CancellationToken cancellationToken);

    /// <summary>
    /// Reads one run with its joined channel data, or <c>null</c> when the run is missing or soft
    /// deleted, so the transport can answer <c>404</c>.
    /// </summary>
    Task<BackfillRunDto?> GetByIdAsync(Guid runId, CancellationToken cancellationToken);
}
