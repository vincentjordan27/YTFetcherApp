using FluentValidation;
using YtFetcher.Application.DiscoveryKeywords.DTO.Request;

namespace YtFetcher.Api.Endpoints.DiscoveryKeywords.Validation;

/// <summary>
/// Transport constraints only: the keyword must be present, must not be blank, and must fit the persisted
/// column. Normalisation and the duplicate policy belong to Application, not here.
/// </summary>
public sealed class CreateDiscoveryKeywordRequestValidator : AbstractValidator<CreateDiscoveryKeywordRequest>
{
    public CreateDiscoveryKeywordRequestValidator()
    {
        RuleFor(request => request.Keyword)
            .NotEmpty()
            .WithMessage("Enter a keyword.")
            .MaximumLength(200)
            .WithMessage("A keyword cannot exceed 200 characters.")
            .OverridePropertyName("keyword");
    }
}
