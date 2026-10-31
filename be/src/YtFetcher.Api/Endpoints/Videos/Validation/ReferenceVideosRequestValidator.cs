using FluentValidation;
using YtFetcher.Application.Videos.DTO.Request;

namespace YtFetcher.Api.Endpoints.Videos.Validation;

/// <summary>
/// Transport validation for the reference list action, aligned with the active list. The sort contract is
/// checked against the shared <see cref="VideoListSortBy"/> and <see cref="VideoListSortDirection"/> names.
/// </summary>
public sealed class ReferenceVideosRequestValidator : AbstractValidator<ReferenceVideosRequest>
{
    public ReferenceVideosRequestValidator()
    {
        RuleFor(request => request.Page)
            .GreaterThanOrEqualTo(1)
            .WithMessage("page must be greater than or equal to 1.")
            .OverridePropertyName("page");

        RuleFor(request => request.PageSize)
            .InclusiveBetween(1, 100)
            .WithMessage("pageSize must be between 1 and 100.")
            .OverridePropertyName("pageSize");

        RuleFor(request => request.SortBy)
            .Must(value => value is null || Enum.GetNames<VideoListSortBy>().Contains(value, StringComparer.OrdinalIgnoreCase))
            .WithMessage("sortBy must be one of: title, views, likes, publishedAt, lastUpdatedAt, referenceSetAt.")
            .OverridePropertyName("sortBy");

        RuleFor(request => request.SortDirection)
            .Must(value => value is null || Enum.GetNames<VideoListSortDirection>().Contains(value, StringComparer.OrdinalIgnoreCase))
            .WithMessage("sortDirection must be asc or desc.")
            .OverridePropertyName("sortDirection");
    }
}
