using FluentValidation;
using YtFetcher.Application.Videos.DTO.Request;

namespace YtFetcher.Api.Endpoints.Videos.Validation;

/// <summary>
/// Transport validation for the mark-reference action: the selection must be present, bounded, and
/// duplicate-free. GUID shape is enforced by model binding on the <see cref="Guid"/> list.
/// </summary>
public sealed class MarkVideosReferenceRequestValidator : AbstractValidator<MarkVideosReferenceRequest>
{
    public const int MaximumSelection = 100;

    public MarkVideosReferenceRequestValidator()
    {
        RuleFor(request => request.VideoIds)
            .NotNull()
            .WithMessage("videoIds is required.")
            .OverridePropertyName("videoIds");

        RuleFor(request => request.VideoIds!)
            .Must(ids => ids is { Count: >= 1 and <= MaximumSelection })
            .When(request => request.VideoIds is not null)
            .WithMessage($"Select between 1 and {MaximumSelection} videos.")
            .OverridePropertyName("videoIds");

        RuleFor(request => request.VideoIds!)
            .Must(ids => ids.Distinct().Count() == ids.Count)
            .When(request => request.VideoIds is not null)
            .WithMessage("The same video cannot be selected more than once.")
            .OverridePropertyName("videoIds");
    }
}
