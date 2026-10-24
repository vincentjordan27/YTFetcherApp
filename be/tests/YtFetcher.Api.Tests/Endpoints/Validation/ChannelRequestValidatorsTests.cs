using YtFetcher.Api.Endpoints.Channels.Validation;
using YtFetcher.Application.Channels.DTO.Request;

namespace YtFetcher.Api.Tests.Endpoints.Validation;

public class ChannelRequestValidatorsTests
{
    [Theory]
    [InlineData(0, 20, "page")]
    [InlineData(1, 0, "pageSize")]
    [InlineData(1, 101, "pageSize")]
    public async Task Pending_list_validator_returns_the_existing_transport_field_name(
        int page,
        int pageSize,
        string expectedPropertyName)
    {
        var result = await new PendingChannelsRequestValidator().ValidateAsync(
            new PendingChannelsRequest(page, pageSize, null));

        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, failure => failure.PropertyName == expectedPropertyName);
    }

    [Fact]
    public async Task Approved_and_rejected_list_validators_apply_the_same_paging_contract()
    {
        var approved = await new ApprovedChannelsRequestValidator().ValidateAsync(
            new ApprovedChannelsRequest(0, 20, null));
        var rejected = await new RejectedChannelsRequestValidator().ValidateAsync(
            new RejectedChannelsRequest(1, 101, null));

        Assert.Contains(approved.Errors, failure => failure.PropertyName == "page");
        Assert.Contains(rejected.Errors, failure => failure.PropertyName == "pageSize");
    }

    [Theory]
    [InlineData("   ")]
    [InlineData("")]
    public async Task Reject_validator_requires_a_non_blank_reason(string reason)
    {
        var result = await new RejectChannelRequestValidator().ValidateAsync(new RejectChannelRequest(reason));

        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, failure => failure.PropertyName == "reason");
    }
}
