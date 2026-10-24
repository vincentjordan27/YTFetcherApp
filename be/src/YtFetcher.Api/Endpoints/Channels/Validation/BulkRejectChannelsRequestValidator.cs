using FluentValidation;
using YtFetcher.Application.Channels.DTO.Request;
using YtFetcher.Core.Channels;

namespace YtFetcher.Api.Endpoints.Channels.Validation;

public sealed class BulkRejectChannelsRequestValidator : AbstractValidator<BulkRejectChannelsRequest>
{
    public BulkRejectChannelsRequestValidator()
    {
        RuleFor(request => request.ChannelIds)
            .NotNull()
            .WithMessage("channelIds is required.")
            .OverridePropertyName("channelIds");

        RuleFor(request => request.ChannelIds!)
            .Must(ids => ids is { Count: >= 1 and <= 100 })
            .When(request => request.ChannelIds is not null)
            .WithMessage("Select between 1 and 100 channels.")
            .OverridePropertyName("channelIds");

        RuleFor(request => request.ChannelIds!)
            .Must(ids => ids.Distinct().Count() == ids.Count)
            .When(request => request.ChannelIds is not null)
            .WithMessage("The same channel cannot be selected more than once.")
            .OverridePropertyName("channelIds");

        RuleFor(request => request.Reason)
            .NotEmpty()
            .WithMessage("A rejection reason is required.")
            .MaximumLength(Channel.RejectionReasonMaxLength)
            .WithMessage($"A rejection reason cannot exceed {Channel.RejectionReasonMaxLength} characters.")
            .OverridePropertyName("reason");
    }
}
