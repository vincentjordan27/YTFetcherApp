using System.Net;
using Microsoft.Extensions.Logging.Abstractions;
using YtFetcher.Application.Abstractions.Services;
using YtFetcher.Application.Channels.DTO.Result;
using YtFetcher.Infrastructure.Configuration;
using YtFetcher.Infrastructure.YouTube;

namespace YtFetcher.Api.Tests.Channels;

/// <summary>
/// The manual-add YouTube adapter: exactly one <c>channels.list</c> request with the
/// <c>forHandle</c> filter, normalized outcomes, and sanitized failures that never leak the key.
/// </summary>
public class YouTubeChannelLookupClientTests
{
    [Fact]
    public async Task The_request_uses_the_forHandle_filter_and_a_valid_channel_is_mapped()
    {
        var (client, handler) = CreateClient(
            "real-key",
            _ => Json(ChannelsResponseJson(
                ChannelItemJson("UC1", "Channel One", "https://example.com/thumb.jpg", "UU-UC1"))));

        var result = await client.LookupByHandleAsync("@example", CancellationToken.None);

        Assert.Equal(ManualChannelLookupStatus.Found, result.Status);
        Assert.Equal("UC1", result.YouTubeChannelId);
        Assert.Equal("Channel One", result.ChannelName);
        Assert.Equal("https://example.com/thumb.jpg", result.ChannelThumbnailUrl);
        Assert.Equal("UU-UC1", result.UploadsPlaylistId);

        var requestUri = Assert.Single(handler.RequestUris);
        Assert.StartsWith("https://www.googleapis.com/youtube/v3/channels?", requestUri);
        Assert.Equal("snippet,contentDetails", QueryValue(requestUri, "part"));
        Assert.Equal("@example", QueryValue(requestUri, "forHandle"));
        Assert.Equal("real-key", QueryValue(requestUri, "key"));
        Assert.Null(QueryValue(requestUri, "id"));
    }

    [Fact]
    public async Task An_empty_items_array_returns_not_found()
    {
        var (client, _) = CreateClient("real-key", _ => Json("""{"items":[]}"""));

        var result = await client.LookupByHandleAsync("@missing", CancellationToken.None);

        Assert.Equal(ManualChannelLookupStatus.NotFound, result.Status);
        Assert.Null(result.YouTubeChannelId);
    }

    [Fact]
    public async Task A_found_channel_without_an_uploads_playlist_is_returned_with_a_null_playlist()
    {
        var (client, _) = CreateClient(
            "real-key",
            _ => Json(ChannelsResponseJson(
                """{"id":"UC1","snippet":{"title":"Channel One"}}""")));

        var result = await client.LookupByHandleAsync("@example", CancellationToken.None);

        Assert.Equal(ManualChannelLookupStatus.Found, result.Status);
        Assert.Null(result.UploadsPlaylistId);
    }

    [Fact]
    public async Task A_provider_error_returns_unavailable_without_leaking_the_key()
    {
        var (client, _) = CreateClient(
            "real-key",
            _ => new HttpResponseMessage(HttpStatusCode.InternalServerError)
            {
                Content = new StringContent("provider exploded with real-key")
            });

        var result = await client.LookupByHandleAsync("@example", CancellationToken.None);

        Assert.Equal(ManualChannelLookupStatus.Unavailable, result.Status);
        Assert.Null(result.YouTubeChannelId);
    }

    [Fact]
    public async Task A_network_error_returns_unavailable()
    {
        var (client, _) = CreateClient("real-key", _ => throw new HttpRequestException("network down"));

        var result = await client.LookupByHandleAsync("@example", CancellationToken.None);

        Assert.Equal(ManualChannelLookupStatus.Unavailable, result.Status);
    }

    [Fact]
    public async Task Malformed_json_returns_unavailable()
    {
        var (client, _) = CreateClient("real-key", _ => Json("<html>not json</html>"));

        var result = await client.LookupByHandleAsync("@example", CancellationToken.None);

        Assert.Equal(ManualChannelLookupStatus.Unavailable, result.Status);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("PUT_YOUR_API_KEY_HERE")]
    [InlineData("CHANGE_ME")]
    public async Task A_missing_or_placeholder_key_returns_unavailable_without_any_http_call(string? apiKey)
    {
        var (client, handler) = CreateClient(apiKey, _ => throw new InvalidOperationException("HTTP must not be called."));

        var result = await client.LookupByHandleAsync("@example", CancellationToken.None);

        Assert.Equal(ManualChannelLookupStatus.Unavailable, result.Status);
        Assert.Empty(handler.RequestUris);
    }

    private static (YouTubeChannelLookupClient Client, StubHttpMessageHandler Handler) CreateClient(
        string? apiKey,
        Func<HttpRequestMessage, HttpResponseMessage> responder)
    {
        var handler = new StubHttpMessageHandler(responder);
        var httpClient = new HttpClient(handler)
        {
            BaseAddress = new Uri("https://www.googleapis.com/youtube/v3/")
        };

        var client = new YouTubeChannelLookupClient(
            httpClient,
            new FakeAppSettingsService(apiKey),
            NullLogger<YouTubeChannelLookupClient>.Instance);

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

    private static string ChannelsResponseJson(params string[] items) =>
        "{\"items\":[" + string.Join(',', items) + "]}";

    private static string ChannelItemJson(string id, string title, string? thumbnailUrl, string? uploadsPlaylistId) =>
        "{\"id\":\"" + id + "\"," +
        "\"snippet\":{\"title\":\"" + title + "\"" +
        (thumbnailUrl is null
            ? string.Empty
            : ",\"thumbnails\":{\"medium\":{\"url\":\"" + thumbnailUrl + "\"}}") +
        "}," +
        (uploadsPlaylistId is null
            ? string.Empty
            : "\"contentDetails\":{\"relatedPlaylists\":{\"uploads\":\"" + uploadsPlaylistId + "\"}},") +
        "\"etag\":\"ignored\"}";

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
