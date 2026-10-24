using FluentValidation;
using YtFetcher.Application.DiscoveryKeywords.DTO.Request;

namespace YtFetcher.Api.Endpoints.DiscoveryKeywords.Validation;

public sealed class DiscoveryKeywordsListRequestValidator : AbstractValidator<DiscoveryKeywordsListRequest>
{
    public DiscoveryKeywordsListRequestValidator()
    {
        RuleFor(request => request.Page)
            .GreaterThanOrEqualTo(1)
            .WithMessage("page must be greater than or equal to 1.")
            .OverridePropertyName("page");

        RuleFor(request => request.PageSize)
            .InclusiveBetween(1, 100)
            .WithMessage("pageSize must be between 1 and 100.")
            .OverridePropertyName("pageSize");
    }
}
