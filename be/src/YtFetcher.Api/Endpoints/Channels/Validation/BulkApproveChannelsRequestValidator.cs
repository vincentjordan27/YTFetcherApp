using FluentValidation;
using YtFetcher.Application.Channels.DTO.Request;

namespace YtFetcher.Api.Endpoints.Channels.Validation;

public sealed class BulkApproveChannelsRequestValidator : AbstractValidator<BulkApproveChannelsRequest>
{
    public BulkApproveChannelsRequestValidator()
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
    }
}
