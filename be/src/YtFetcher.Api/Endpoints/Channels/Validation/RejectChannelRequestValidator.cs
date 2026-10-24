using FluentValidation;
using YtFetcher.Application.Channels.DTO.Request;
using YtFetcher.Core.Channels;

namespace YtFetcher.Api.Endpoints.Channels.Validation;

public sealed class RejectChannelRequestValidator : AbstractValidator<RejectChannelRequest>
{
    public RejectChannelRequestValidator()
    {
        RuleFor(request => request.Reason)
            .Must(reason => !string.IsNullOrWhiteSpace(reason))
            .WithMessage("A rejection reason is required.")
            .OverridePropertyName("reason");

        RuleFor(request => request.Reason)
            .MaximumLength(Channel.RejectionReasonMaxLength)
            .When(request => !string.IsNullOrWhiteSpace(request.Reason))
            .WithMessage($"A rejection reason cannot exceed {Channel.RejectionReasonMaxLength} characters.")
            .OverridePropertyName("reason");
    }
}
