namespace YtFetcher.Application.BackfillRuns.DTO.Response;

/// <summary>Response data for one page of backfill run history.</summary>
public sealed record BackfillRunsResponse(
    IReadOnlyList<BackfillRunDto> Items,
    int Page,
    int PageSize,
    int TotalCount);
