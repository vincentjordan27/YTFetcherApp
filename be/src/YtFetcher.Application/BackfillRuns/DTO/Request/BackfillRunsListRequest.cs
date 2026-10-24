using YtFetcher.Core.BackfillRuns;

namespace YtFetcher.Application.BackfillRuns.DTO.Request;

/// <summary>
/// Request data for one page of backfill run history. Every filter is optional except the page and page
/// size. <see cref="DateFrom"/> and <see cref="DateTo"/> are inclusive UTC days applied to the run's
/// start instant; the transport validator owns the page, page size, and date-range checks.
/// </summary>
public sealed record BackfillRunsListRequest(
    int Page,
    int PageSize,
    string? SearchText,
    BackfillRunStatus? Status,
    BackfillTriggerSource? TriggerSource,
    DateOnly? DateFrom,
    DateOnly? DateTo);
