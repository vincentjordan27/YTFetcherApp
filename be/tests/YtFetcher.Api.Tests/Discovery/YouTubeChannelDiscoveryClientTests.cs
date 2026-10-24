using System.Net;
using System.Text.Json;
using Microsoft.Extensions.Logging.Abstractions;
using YtFetcher.Api.Tests.Channels;
using YtFetcher.Application.Abstractions.Services;
using YtFetcher.Infrastructure.Configuration;
using YtFetcher.Infrastructure.YouTube;

namespace YtFetcher.Api.Tests.Discovery;

/// <summary>
/// The discovery client now exposes two provider calls that consume different buckets: <c>search.list</c> and
/// one <c>channels.list</c> metadata chunk. These tests pin each request shape and every normalized outcome.
/// </summary>
public class YouTubeChannelDiscoveryClientTests
{
    [Fact]
    public async Task Search_uses_the_expected_shape_and_maps_valid_json()
    {
        var responses = new Queue<HttpResponseMessage>([
            Json(SearchResponseJson(
                "next-1",
                [SearchItemJson("v1", "UC1", "Title 1", "2026-09-01T00:00:00Z")]))
        ]);
        var (client, handler) = CreateClient("real-key", _ => responses.Dequeue());

        var result = await client.SearchAsync("sakura", "page-token", CancellationToken.None);

        Assert.True(result.Succeeded);
        Assert.Equal("next-1", result.NextPageToken);
        Assert.Equal(0, result.MalformedItemsSkipped);

        var item = Assert.Single(result.Items);
        Assert.Equal("UC1", item.YouTubeChannelId);
        Assert.Equal("v1", item.YouTubeVideoId);
        Assert.Equal("Title 1", item.VideoTitle);
        Assert.Equal("https://example.com/v1-high.jpg", item.VideoThumbnailUrl);
        Assert.Equal(new DateTimeOffset(2026, 9, 1, 0, 0, 0, TimeSpan.Zero), item.VideoPublishedAt);

        var searchUri = Assert.Single(handler.RequestUris);
        Assert.StartsWith("https://www.googleapis.com/youtube/v3/search?", searchUri);
        Assert.Equal("snippet", QueryValue(searchUri, "part"));
        Assert.Equal("video", QueryValue(searchUri, "type"));
        Assert.Equal("short", QueryValue(searchUri, "videoDuration"));
        Assert.Equal("date", QueryValue(searchUri, "order"));
        Assert.Equal("25", QueryValue(searchUri, "maxResults"));
        Assert.Equal("sakura", QueryValue(searchUri, "q"));
        Assert.Equal("page-token", QueryValue(searchUri, "pageToken"));
        Assert.Equal("real-key", QueryValue(searchUri, "key"));
    }

    [Fact]
    public async Task Metadata_lookup_uses_the_expected_shape_and_maps_valid_metadata()
    {
        var responses = new Queue<HttpResponseMessage>([
            Json(ChannelsResponseJson([("UC1", "Channel One")]))
        ]);
        var (client, handler) = CreateClient("real-key", _ => responses.Dequeue());

        var result = await client.GetChannelMetadataAsync(["UC1"], CancellationToken.None);

        Assert.True(result.Succeeded);
        var item = Assert.Single(result.Items);
        Assert.Equal("UC1", item.YouTubeChannelId);
        Assert.Equal("Channel One", item.ChannelName);
        Assert.Equal("https://example.com/UC1-default.jpg", item.ChannelThumbnailUrl);
        Assert.Equal("UU-UC1", item.UploadsPlaylistId);

        var channelsUri = Assert.Single(handler.RequestUris);
        Assert.StartsWith("https://www.googleapis.com/youtube/v3/channels?", channelsUri);
        Assert.Equal("snippet,contentDetails", QueryValue(channelsUri, "part"));
        Assert.Equal("UC1", QueryValue(channelsUri, "id"));
        Assert.Equal("real-key", QueryValue(channelsUri, "key"));
    }

    [Fact]
    public async Task An_empty_metadata_id_set_makes_no_http_call()
    {
        var (client, handler) = CreateClient("real-key", _ => throw new InvalidOperationException("HTTP must not be called."));

        var result = await client.GetChannelMetadataAsync([], CancellationToken.None);

        Assert.True(result.Succeeded);
        Assert.Empty(result.Items);
        Assert.Empty(handler.RequestUris);
    }

    [Fact]
    public async Task A_successful_youtube_api_response_is_not_logged_at_any_level()
    {
        var logSink = new TestLogSink();
        var responses = new Queue<HttpResponseMessage>([
            Json(SearchResponseJson(null, [SearchItemJson("v1", "UC1", "Title 1", "2026-09-01T00:00:00Z")]))
        ]);
        var (client, _) = CreateClient("real-key", _ => responses.Dequeue(), logSink);

        await client.SearchAsync("sakura", null, CancellationToken.None);

        Assert.DoesNotContain(
            logSink.Entries,
            entry => entry.Message.StartsWith("YouTubeApiResponseReceived", StringComparison.Ordinal));
        Assert.DoesNotContain(
            logSink.Entries,
            entry => entry.Properties.ContainsKey("ResponseBody"));
    }

    [Fact]
    public async Task Malformed_search_items_are_skipped_and_counted()
    {
        var logSink = new TestLogSink();
        var responses = new Queue<HttpResponseMessage>([
            Json(SearchResponseJson(
                null,
                [
                    SearchItemJson("v1", "UC1", "Title 1", "2026-09-01T00:00:00Z"),
                    """{"id":{"kind":"youtube#video"},"snippet":{"channelId":"UC2","title":"No video id"}}"""
                ]))
        ]);
        var (client, _) = CreateClient("real-key", _ => responses.Dequeue(), logSink);

        var result = await client.SearchAsync("sakura", null, CancellationToken.None);

        Assert.True(result.Succeeded);
        Assert.Equal(1, result.MalformedItemsSkipped);
        Assert.Single(result.Items);

        var warning = Assert.Single(
            logSink.Entries,
            entry => entry.Message.StartsWith("ChannelDiscoveryMalformedItemsSkipped", StringComparison.Ordinal));
        Assert.Equal("sakura", warning.Properties["Keyword"]);
        Assert.Equal(1, warning.Properties["SkippedCount"]);
    }

    [Fact]
    public async Task Metadata_lookup_rejects_more_than_fifty_ids()
    {
        var (client, handler) = CreateClient("real-key", _ => throw new InvalidOperationException("HTTP must not be called."));
        var channelIds = Enumerable.Range(0, 51).Select(index => $"UC{index:D3}").ToList();

        await Assert.ThrowsAsync<ArgumentException>(
            () => client.GetChannelMetadataAsync(channelIds, CancellationToken.None));

        Assert.Empty(handler.RequestUris);
    }

    [Fact]
    public async Task Quota_exceeded_returns_a_sanitized_failure_for_both_calls()
    {
        var (searchClient, _) = CreateClient("real-key", _ => QuotaExceededResponse());

        var searchResult = await searchClient.SearchAsync("sakura", null, CancellationToken.None);

        Assert.False(searchResult.Succeeded);
        Assert.Equal("QuotaExceeded", searchResult.FailureReason);
        Assert.DoesNotContain("real-key", searchResult.FailureReason);

        var (metadataClient, _) = CreateClient("real-key", _ => QuotaExceededResponse());

        var metadataResult = await metadataClient.GetChannelMetadataAsync(["UC1"], CancellationToken.None);

        Assert.False(metadataResult.Succeeded);
        Assert.Equal("QuotaExceeded", metadataResult.FailureReason);
    }

    [Fact]
    public async Task Non_success_response_returns_a_sanitized_failure()
    {
        var (client, _) = CreateClient(
            "real-key",
            _ => new HttpResponseMessage(HttpStatusCode.InternalServerError)
            {
                Content = new StringContent("server exploded with real-key")
            });

        var result = await client.SearchAsync("sakura", null, CancellationToken.None);

        Assert.False(result.Succeeded);
        Assert.Equal("HttpError", result.FailureReason);
        Assert.DoesNotContain("real-key", result.FailureReason);
    }

    [Fact]
    public async Task Timeout_returns_a_sanitized_failure()
    {
        var (client, _) = CreateClient("real-key", _ => throw new TaskCanceledException("timed out"));

        var result = await client.SearchAsync("sakura", null, CancellationToken.None);

        Assert.False(result.Succeeded);
        Assert.Equal("Timeout", result.FailureReason);
    }

    [Fact]
    public async Task Network_error_returns_a_sanitized_failure()
    {
        var (client, _) = CreateClient("real-key", _ => throw new HttpRequestException("network down"));

        var result = await client.SearchAsync("sakura", null, CancellationToken.None);

        Assert.False(result.Succeeded);
        Assert.Equal("NetworkError", result.FailureReason);
    }

    [Fact]
    public async Task Malformed_json_returns_a_sanitized_failure()
    {
        var (client, _) = CreateClient("real-key", _ => Json("<html>not json</html>"));

        var result = await client.SearchAsync("sakura", null, CancellationToken.None);

        Assert.False(result.Succeeded);
        Assert.Equal("MalformedResponse", result.FailureReason);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("PUT_YOUR_API_KEY_HERE")]
    [InlineData("CHANGE_ME")]
    public async Task Missing_or_placeholder_key_fails_without_any_http_call(string? apiKey)
    {
        var (client, handler) = CreateClient(apiKey, _ => throw new InvalidOperationException("HTTP must not be called."));

        var searchResult = await client.SearchAsync("sakura", null, CancellationToken.None);
        var metadataResult = await client.GetChannelMetadataAsync(["UC1"], CancellationToken.None);

        Assert.False(searchResult.Succeeded);
        Assert.Equal("MissingApiKey", searchResult.FailureReason);
        Assert.False(metadataResult.Succeeded);
        Assert.Equal("MissingApiKey", metadataResult.FailureReason);
        Assert.Empty(handler.RequestUris);
    }

    private static HttpResponseMessage QuotaExceededResponse() => new(HttpStatusCode.Forbidden)
    {
        Content = new StringContent("""{"error":{"errors":[{"reason":"quotaExceeded"}]}}""")
    };

    private static (YouTubeChannelDiscoveryClient Client, StubHttpMessageHandler Handler) CreateClient(
        string? apiKey,
        Func<HttpRequestMessage, HttpResponseMessage> responder,
        TestLogSink? logSink = null)
    {
        var handler = new StubHttpMessageHandler(responder);
        var httpClient = new HttpClient(handler)
        {
            BaseAddress = new Uri("https://www.googleapis.com/youtube/v3/")
        };

        var client = new YouTubeChannelDiscoveryClient(
            httpClient,
            new FakeAppSettingsService(apiKey),
            logSink is null
                ? NullLogger<YouTubeChannelDiscoveryClient>.Instance
                : logSink.CreateLogger<YouTubeChannelDiscoveryClient>());

        return (client, handler);
    }

    private static HttpResponseMessage Json(string body) => new(HttpStatusCode.OK)
    {
        Content = new StringContent(body)
    };

    private static string? QueryValue(string uri, string name)
    {
        var query = new Uri(uri).Query.TrimStart('?');

        foreach (var part in query.Split('&'))
        {
            var keyValue = part.Split('=', 2);
            if (keyValue.Length == 2 && Uri.UnescapeDataString(keyValue[0]) == name)
            {
                return Uri.UnescapeDataString(keyValue[1]);
            }
        }

        return null;
    }

    private static string SearchResponseJson(string? nextPageToken, IEnumerable<string> items)
    {
        var token = nextPageToken is null ? "null" : JsonSerializer.Serialize(nextPageToken);
        return "{\"nextPageToken\":" + token + ",\"items\":[" + string.Join(',', items) + "]}";
    }

    private static string SearchItemJson(string videoId, string channelId, string title, string publishedAt) =>
        "{\"id\":{\"kind\":\"youtube#video\",\"videoId\":\"" + videoId + "\"}," +
        "\"snippet\":{\"publishedAt\":\"" + publishedAt + "\",\"channelId\":\"" + channelId +
        "\",\"title\":\"" + title + "\",\"thumbnails\":{\"high\":{\"url\":\"https://example.com/" +
        videoId + "-high.jpg\"}}}}";

    private static string ChannelsResponseJson(IEnumerable<(string ChannelId, string Title)> channels) =>
        "{\"items\":[" + string.Join(',', channels.Select(ChannelItemJson)) + "]}";

    private static string ChannelItemJson((string ChannelId, string Title) channel) =>
        "{\"id\":\"" + channel.ChannelId + "\"," +
        "\"snippet\":{\"title\":\"" + channel.Title + "\",\"thumbnails\":{\"default\":{\"url\":\"https://example.com/" +
        channel.ChannelId + "-default.jpg\"},\"high\":{\"url\":\"https://example.com/" +
        channel.ChannelId + "-high.jpg\"}}}," +
        "\"contentDetails\":{\"relatedPlaylists\":{\"uploads\":\"UU-" + channel.ChannelId + "\"}}}";

    private sealed class StubHttpMessageHandler(Func<HttpRequestMessage, HttpResponseMessage> responder)
        : HttpMessageHandler
    {
        public List<string> RequestUris { get; } = [];

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            RequestUris.Add(request.RequestUri!.ToString());
            return Task.FromResult(responder(request));
        }
    }

    private sealed class FakeAppSettingsService(string? apiKey) : IAppSettingsService
    {
        public string? GetValue(string key) =>
            key == YouTubeConfigurationKeys.ApiKey ? apiKey : null;

        public string GetRequiredValue(string key) =>
            GetValue(key) ?? throw new InvalidOperationException($"Missing {key}.");

        public T GetRequiredSection<T>(string sectionName) where T : class =>
            throw new NotSupportedException();
    }
}
