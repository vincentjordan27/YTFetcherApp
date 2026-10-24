using FluentValidation;
using YtFetcher.Application.BackfillRuns.DTO.Request;

namespace YtFetcher.Api.Endpoints.BackfillRuns.Validation;

/// <summary>
/// Transport constraints for the run-history list: paging bounds and a non-inverted date window. The
/// date comparison is written on the whole request because the two bounds are nullable.
/// </summary>
public sealed class BackfillRunsListRequestValidator : AbstractValidator<BackfillRunsListRequest>
{
    public BackfillRunsListRequestValidator()
    {
        RuleFor(request => request.Page)
            .GreaterThanOrEqualTo(1)
            .WithMessage("page must be greater than or equal to 1.")
            .OverridePropertyName("page");

        RuleFor(request => request.PageSize)
            .InclusiveBetween(1, 100)
            .WithMessage("pageSize must be between 1 and 100.")
            .OverridePropertyName("pageSize");

        RuleFor(request => request)
            .Must(request =>
                request.DateFrom is null ||
                request.DateTo is null ||
                request.DateFrom.Value <= request.DateTo.Value)
            .WithMessage("dateTo must be on or after dateFrom.")
            .OverridePropertyName("dateTo");
    }
}
