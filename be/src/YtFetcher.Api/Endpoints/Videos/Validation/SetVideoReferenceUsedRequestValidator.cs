using FluentValidation;
using YtFetcher.Application.Videos.DTO.Request;

namespace YtFetcher.Api.Endpoints.Videos.Validation;

/// <summary>Validates an explicit target usage state for one Reference video.</summary>
public sealed class SetVideoReferenceUsedRequestValidator : AbstractValidator<SetVideoReferenceUsedRequest>
{
    public SetVideoReferenceUsedRequestValidator()
    {
        RuleFor(request => request.VideoId)
            .NotEmpty()
            .WithMessage("videoId is required.")
            .OverridePropertyName("videoId");

        RuleFor(request => request.IsUsed)
            .NotNull()
            .WithMessage("isUsed is required.")
            .OverridePropertyName("isUsed");
    }
}
