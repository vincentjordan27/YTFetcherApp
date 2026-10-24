using FluentValidation;
using YtFetcher.Application.Channels.DTO.Request;

namespace YtFetcher.Api.Endpoints.Channels.Validation;

/// <summary>
/// Transport-shape validation for the manual-add action only: the field must be present and bounded.
/// Accepted reference formats (<c>@handle</c> only) are Application behavior.
/// </summary>
public sealed class ManualApproveChannelRequestValidator : AbstractValidator<ManualApproveChannelRequest>
{
    public const int MaximumReferenceLength = 500;

    public ManualApproveChannelRequestValidator()
    {
        RuleFor(request => request.ChannelReference)
            .NotEmpty()
            .WithMessage("Enter a YouTube handle starting with @.")
            .OverridePropertyName("channelReference");

        RuleFor(request => request.ChannelReference)
            .MaximumLength(MaximumReferenceLength)
            .When(request => !string.IsNullOrEmpty(request.ChannelReference))
            .WithMessage($"A YouTube handle cannot exceed {MaximumReferenceLength} characters.")
            .OverridePropertyName("channelReference");
    }
}
