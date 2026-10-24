using System.Net;
using System.Net.Http.Json;
using YtFetcher.Api.Tests.Channels;

namespace YtFetcher.Api.Tests.DiscoveryKeywords;

/// <summary>
/// HTTP contract of the live discovery keyword management: the body-based paged list, creation with the
/// exact duplicate sentence, the selection toggles, and the soft delete that answers 204.
/// </summary>
[Collection(ApiStartupCollection.Name)]
public class DiscoveryKeywordEndpointTests
{
    private const string DuplicateMessage = "This keyword already exists or was previously removed.";

    [Fact]
    public async Task Create_returns_201_with_the_trimmed_active_keyword_and_the_list_shows_it()
    {
        using var configuration = new HostConfigurationScope();
        using var application = new ChannelApiFactory();
        using var client = application.CreateClient();

        var created = await client.PostAsJsonAsync("/api/discovery-keywords/", new { keyword = "  sakura  " });

        Assert.Equal(HttpStatusCode.Created, created.StatusCode);
        var item = await created.Content.ReadFromJsonAsync<KeywordPayload>();
        Assert.NotNull(item);
        Assert.Equal("sakura", item.Keyword);
        Assert.True(item.IsActive);

        var page = await client.PostAsJsonAsync(
            "/api/discovery-keywords/list",
            new { page = 1, pageSize = 20, searchText = (string?)null });
        Assert.Equal(HttpStatusCode.OK, page.StatusCode);
        var list = await page.Content.ReadFromJsonAsync<KeywordPagePayload>();
        Assert.NotNull(list);
        Assert.Single(list.Items);
        Assert.Equal("sakura", list.Items[0].Keyword);
        Assert.Equal(1, list.TotalCount);
        Assert.Equal(1, list.ActiveCount);
    }

    [Fact]
    public async Task Create_returns_409_with_the_exact_sentence_for_any_existing_form()
    {
        using var configuration = new HostConfigurationScope();
        using var application = new ChannelApiFactory();
        using var client = application.CreateClient();

        var first = await client.PostAsJsonAsync("/api/discovery-keywords/", new { keyword = "sakura" });
        var duplicate = await client.PostAsJsonAsync("/api/discovery-keywords/", new { keyword = "SAKURA" });

        Assert.Equal(HttpStatusCode.Created, first.StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, duplicate.StatusCode);

        var problem = await duplicate.Content.ReadFromJsonAsync<ProblemPayload>();
        Assert.NotNull(problem);
        Assert.Equal(DuplicateMessage, problem.Detail);
    }

    [Fact]
    public async Task Create_returns_400_with_the_keyword_field_for_a_blank_value()
    {
        using var configuration = new HostConfigurationScope();
        using var application = new ChannelApiFactory();
        using var client = application.CreateClient();

        var response = await client.PostAsJsonAsync("/api/discovery-keywords/", new { keyword = "   " });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var problem = await response.Content.ReadFromJsonAsync<ValidationProblemPayload>();
        Assert.NotNull(problem);
        Assert.True(problem.Errors.ContainsKey("keyword"));
    }

    [Fact]
    public async Task The_list_returns_400_for_an_out_of_range_page_or_page_size()
    {
        using var configuration = new HostConfigurationScope();
        using var application = new ChannelApiFactory();
        using var client = application.CreateClient();

        var badPage = await client.PostAsJsonAsync(
            "/api/discovery-keywords/list",
            new { page = 0, pageSize = 20, searchText = (string?)null });
        var badSize = await client.PostAsJsonAsync(
            "/api/discovery-keywords/list",
            new { page = 1, pageSize = 101, searchText = (string?)null });

        Assert.Equal(HttpStatusCode.BadRequest, badPage.StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, badSize.StatusCode);
    }

    [Fact]
    public async Task Disable_and_enable_toggle_the_selection_flag_and_the_counts_follow()
    {
        using var configuration = new HostConfigurationScope();
        using var application = new ChannelApiFactory();
        using var client = application.CreateClient();
        var created = await client.PostAsJsonAsync("/api/discovery-keywords/", new { keyword = "sakura" });
        var item = await created.Content.ReadFromJsonAsync<KeywordPayload>();
        Assert.NotNull(item);

        var disabled = await client.PostAsJsonAsync($"/api/discovery-keywords/{item.Id}/disable", new { });
        Assert.Equal(HttpStatusCode.OK, disabled.StatusCode);
        var disabledItem = await disabled.Content.ReadFromJsonAsync<KeywordPayload>();
        Assert.NotNull(disabledItem);
        Assert.False(disabledItem.IsActive);

        var afterDisable = await ReadPageAsync(client);
        Assert.Equal(1, afterDisable.TotalCount);
        Assert.Equal(0, afterDisable.ActiveCount);

        var enabled = await client.PostAsJsonAsync($"/api/discovery-keywords/{item.Id}/enable", new { });
        Assert.Equal(HttpStatusCode.OK, enabled.StatusCode);
        var enabledItem = await enabled.Content.ReadFromJsonAsync<KeywordPayload>();
        Assert.NotNull(enabledItem);
        Assert.True(enabledItem.IsActive);

        var afterEnable = await ReadPageAsync(client);
        Assert.Equal(1, afterEnable.ActiveCount);
    }

    [Fact]
    public async Task Remove_answers_204_and_the_keyword_leaves_the_list_but_still_blocks_creation()
    {
        using var configuration = new HostConfigurationScope();
        using var application = new ChannelApiFactory();
        using var client = application.CreateClient();
        var created = await client.PostAsJsonAsync("/api/discovery-keywords/", new { keyword = "sakura" });
        var item = await created.Content.ReadFromJsonAsync<KeywordPayload>();
        Assert.NotNull(item);

        var removed = await client.DeleteAsync($"/api/discovery-keywords/{item.Id}");

        Assert.Equal(HttpStatusCode.NoContent, removed.StatusCode);
        var page = await ReadPageAsync(client);
        Assert.Empty(page.Items);
        Assert.Equal(0, page.TotalCount);

        // A removed keyword is never revived, so creating it again is still the duplicate conflict.
        var recreate = await client.PostAsJsonAsync("/api/discovery-keywords/", new { keyword = "sakura" });
        Assert.Equal(HttpStatusCode.Conflict, recreate.StatusCode);
        var problem = await recreate.Content.ReadFromJsonAsync<ProblemPayload>();
        Assert.NotNull(problem);
        Assert.Equal(DuplicateMessage, problem.Detail);
    }

    [Fact]
    public async Task An_unknown_keyword_is_not_found_for_every_mutation()
    {
        using var configuration = new HostConfigurationScope();
        using var application = new ChannelApiFactory();
        using var client = application.CreateClient();
        var unknown = Guid.NewGuid();

        Assert.Equal(
            HttpStatusCode.NotFound,
            (await client.PostAsJsonAsync($"/api/discovery-keywords/{unknown}/disable", new { })).StatusCode);
        Assert.Equal(
            HttpStatusCode.NotFound,
            (await client.PostAsJsonAsync($"/api/discovery-keywords/{unknown}/enable", new { })).StatusCode);
        Assert.Equal(
            HttpStatusCode.NotFound,
            (await client.DeleteAsync($"/api/discovery-keywords/{unknown}")).StatusCode);
    }

    [Fact]
    public async Task An_invalid_keyword_id_is_a_validation_problem()
    {
        using var configuration = new HostConfigurationScope();
        using var application = new ChannelApiFactory();
        using var client = application.CreateClient();

        var response = await client.PostAsJsonAsync("/api/discovery-keywords/not-a-guid/disable", new { });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var problem = await response.Content.ReadFromJsonAsync<ValidationProblemPayload>();
        Assert.NotNull(problem);
        Assert.True(problem.Errors.ContainsKey("keywordId"));
    }

    private static async Task<KeywordPagePayload> ReadPageAsync(HttpClient client)
    {
        var response = await client.PostAsJsonAsync(
            "/api/discovery-keywords/list",
            new { page = 1, pageSize = 20, searchText = (string?)null });
        response.EnsureSuccessStatusCode();
        var page = await response.Content.ReadFromJsonAsync<KeywordPagePayload>();
        Assert.NotNull(page);
        return page;
    }

    private sealed record KeywordPayload(Guid Id, string Keyword, bool IsActive);

    private sealed record KeywordPagePayload(
        IReadOnlyList<KeywordPayload> Items,
        int Page,
        int PageSize,
        int TotalCount,
        int ActiveCount);

    private sealed record ProblemPayload(string? Title, string? Detail);

    private sealed record ValidationProblemPayload(IDictionary<string, string[]> Errors);
}
