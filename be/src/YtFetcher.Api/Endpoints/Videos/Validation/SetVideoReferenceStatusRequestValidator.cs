using FluentValidation;
using YtFetcher.Application.Videos.DTO.Request;
using YtFetcher.Core.Videos;

namespace YtFetcher.Api.Endpoints.Videos.Validation;

/// <summary>Validates a target curation state for one Reference video.</summary>
public sealed class SetVideoReferenceStatusRequestValidator : AbstractValidator<SetVideoReferenceStatusRequest>
{
    public SetVideoReferenceStatusRequestValidator()
    {
        RuleFor(request => request.VideoId)
            .NotEmpty()
            .WithMessage("videoId is required.")
            .OverridePropertyName("videoId");

        RuleFor(request => request.ReferenceStatus)
            .NotNull()
            .Must(status => status is VideoReferenceStatus.UNUSED or VideoReferenceStatus.USED or VideoReferenceStatus.SKIPPED)
            .WithMessage("referenceStatus must be UNUSED, USED, or SKIPPED.")
            .OverridePropertyName("referenceStatus");
    }
}
