using YtFetcher.Api.Endpoints.Videos.Validation;
using YtFetcher.Application.Videos.DTO.Request;

namespace YtFetcher.Api.Tests.Endpoints.Validation;

/// <summary>
/// Transport validation for the active list action: the established paging field names and the sort
/// contract, which must reject anything outside the shared sort enum names.
/// </summary>
public class ActiveVideosRequestValidatorTests
{
    [Theory]
    [InlineData(0, 10, "page")]
    [InlineData(1, 0, "pageSize")]
    [InlineData(1, 101, "pageSize")]
    public async Task Active_list_validator_returns_the_established_transport_field_name(
        int page,
        int pageSize,
        string expectedPropertyName)
    {
        var result = await new ActiveVideosRequestValidator().ValidateAsync(
            new ActiveVideosRequest(page, pageSize, null, null, null));

        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, failure => failure.PropertyName == expectedPropertyName);
    }

    [Fact]
    public async Task Active_list_validator_accepts_the_documented_paging_range_and_sort()
    {
        var result = await new ActiveVideosRequestValidator().ValidateAsync(
            new ActiveVideosRequest(1, 100, "sakura", "publishedAt", "asc"));

        Assert.True(result.IsValid);
    }

    [Fact]
    public async Task Active_list_validator_accepts_an_absent_sort_because_the_default_applies()
    {
        var result = await new ActiveVideosRequestValidator().ValidateAsync(
            new ActiveVideosRequest(1, 10, null, null, null));

        Assert.True(result.IsValid);
    }

    [Theory]
    [InlineData("title")]
    [InlineData("views")]
    [InlineData("likes")]
    [InlineData("publishedAt")]
    [InlineData("lastUpdatedAt")]
    [InlineData("VIEWS")]
    public async Task Active_list_validator_accepts_every_permitted_sort_field(string sortBy)
    {
        var result = await new ActiveVideosRequestValidator().ValidateAsync(
            new ActiveVideosRequest(1, 10, null, sortBy, "desc"));

        Assert.True(result.IsValid);
    }

    [Theory]
    [InlineData("channelName")]
    [InlineData("status")]
    [InlineData("age")]
    [InlineData("")]
    [InlineData("3")]
    public async Task Active_list_validator_rejects_an_unknown_sort_field(string sortBy)
    {
        var result = await new ActiveVideosRequestValidator().ValidateAsync(
            new ActiveVideosRequest(1, 10, null, sortBy, "desc"));

        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, failure => failure.PropertyName == "sortBy");
    }

    [Theory]
    [InlineData("sideways")]
    [InlineData("ascending")]
    [InlineData("")]
    [InlineData("1")]
    public async Task Active_list_validator_rejects_an_unknown_sort_direction(string sortDirection)
    {
        var result = await new ActiveVideosRequestValidator().ValidateAsync(
            new ActiveVideosRequest(1, 10, null, "views", sortDirection));

        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, failure => failure.PropertyName == "sortDirection");
    }
}
