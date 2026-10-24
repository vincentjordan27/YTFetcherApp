using FluentValidation;
using YtFetcher.Application.Videos.DTO.Request;

namespace YtFetcher.Api.Endpoints.Videos.Validation;

/// <summary>
/// Transport validation for the remove-reference action. It checks only the GUID shape; whether the row is
/// a reference video is a domain and persistence decision, never a validation concern.
/// </summary>
public sealed class UnmarkVideoReferenceRequestValidator : AbstractValidator<UnmarkVideoReferenceRequest>
{
    public UnmarkVideoReferenceRequestValidator()
    {
        RuleFor(request => request.VideoId)
            .NotEmpty()
            .WithMessage("videoId is required.")
            .OverridePropertyName("videoId");
    }
}
