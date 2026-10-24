using System.Net;
using System.Text.Json;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using YtFetcher.Application.Abstractions.Services;
using YtFetcher.Application.Backfill.DTO.Result;
using YtFetcher.Infrastructure.Configuration;
using YtFetcher.Infrastructure.YouTube;

namespace YtFetcher.Api.Tests.Backfill;

/// <summary>
/// The backfill provider client maps raw YouTube JSON into normalized Application results, keeps the
/// fixed safe failure texts, and never lets a raw provider body or the API key escape.
/// </summary>
public class YouTubeBackfillClientTests
{
    [Fact]
    public async Task Uploads_playlist_resolution_requests_content_details_and_maps_the_id()
    {
        var (client, handler) = CreateClient(Json(
            """{"items":[{"id":"UC1","contentDetails":{"relatedPlaylists":{"uploads":"UU-UC1"}}}]}"""));

        var result = await client.ResolveUploadsPlaylistAsync("UC1", CancellationToken.None);

        Assert.Equal(BackfillProviderOutcome.Succeeded, result.Outcome);
        Assert.Equal("UU-UC1", result.UploadsPlaylistId);
        Assert.Null(result.FailureReason);

        var uri = Assert.Single(handler.RequestUris);
        Assert.StartsWith("https://www.googleapis.com/youtube/v3/channels?", uri);
        Assert.Equal("contentDetails", QueryValue(uri, "part"));
        Assert.Equal("UC1", QueryValue(uri, "id"));
        Assert.Equal("real-key", QueryValue(uri, "key"));
    }

    [Fact]
    public async Task A_channel_without_an_uploads_playlist_is_reported_as_not_found_with_a_safe_reason()
    {
        var (client, _) = CreateClient(Json("""{"items":[{"id":"UC1"}]}"""));

        var result = await client.ResolveUploadsPlaylistAsync("UC1", CancellationToken.None);

        Assert.Equal(BackfillProviderOutcome.NotFound, result.Outcome);
        Assert.Null(result.UploadsPlaylistId);
        Assert.Equal("Unable to find this channel's uploads playlist.", result.FailureReason);
    }

    [Fact]
    public async Task Uploads_page_requests_fifty_items_newest_first_and_normalizes_every_item()
    {
        var (client, handler) = CreateClient(Json(
            """
            {"nextPageToken":"page-2","items":[
              {"contentDetails":{"videoId":"v1"},"snippet":{"publishedAt":"2026-09-01T00:00:00Z","title":"Title 1","thumbnails":{"high":{"url":"https://example.com/v1-high.jpg"}}}},
              {"contentDetails":{},"snippet":{"title":"Malformed"}},
              {"snippet":{"publishedAt":"not-a-timestamp","title":"No timestamp"}}
            ]}
            """));

        var result = await client.GetUploadsPageAsync("UU-UC1", null, CancellationToken.None);

        Assert.Equal(BackfillProviderOutcome.Succeeded, result.Outcome);
        Assert.Equal("page-2", result.NextPageToken);
        Assert.Equal(3, result.Items.Count);

        var first = result.Items[0];
        Assert.Equal("v1", first.YoutubeVideoId);
        Assert.Equal("Title 1", first.Title);
        Assert.Equal("https://example.com/v1-high.jpg", first.ThumbnailUrl);
        Assert.Equal(new DateTimeOffset(2026, 9, 1, 0, 0, 0, TimeSpan.Zero), first.PublishedAt);

        // Malformed provider entries are passed through for the use case to discard.
        Assert.Equal(string.Empty, result.Items[1].YoutubeVideoId);
        Assert.Null(result.Items[2].PublishedAt);

        var uri = Assert.Single(handler.RequestUris);
        Assert.StartsWith("https://www.googleapis.com/youtube/v3/playlistItems?", uri);
        Assert.Equal("snippet,contentDetails", QueryValue(uri, "part"));
        Assert.Equal("UU-UC1", QueryValue(uri, "playlistId"));
        Assert.Equal("50", QueryValue(uri, "maxResults"));
        Assert.Equal("real-key", QueryValue(uri, "key"));
    }

    [Fact]
    public async Task Statistics_requests_use_the_batch_endpoint_and_normalize_numeric_strings()
    {
        var (client, handler) = CreateClient(Json(
            """
            {"items":[
              {"id":"v1","contentDetails":{"durationMillis":"120000"},"statistics":{"viewCount":"1500","likeCount":"42"}},
              {"id":"v2","contentDetails":{},"statistics":{}},
              {"id":"v3","contentDetails":{"durationMillis":90000},"statistics":{"viewCount":"-5"}},
              {"id":"v4","contentDetails":{"duration":"15s"},"statistics":{"viewCount":"2395","likeCount":"27"}},
              {"contentDetails":{"durationMillis":"1000"},"statistics":{"viewCount":"1"}}
            ]}
            """));

        var result = await client.GetVideoStatsAsync(["v1", "v2", "v3", "v4"], CancellationToken.None);

        Assert.Equal(BackfillProviderOutcome.Succeeded, result.Outcome);
        Assert.Equal(4, result.Items.Count);

        Assert.Equal(new VideoStatsItemResult("v1", 120_000, 1_500, 42), result.Items[0]);

        // Absent public numeric values become zero and a missing duration stays invalid.
        Assert.Equal(new VideoStatsItemResult("v2", -1, 0, 0), result.Items[1]);

        // Negative provider values are clamped to zero.
        Assert.Equal(new VideoStatsItemResult("v3", 90_000, 0, 0), result.Items[2]);

        // The live batch endpoint currently returns bare seconds, for example "15s".
        Assert.Equal(new VideoStatsItemResult("v4", 15_000, 2_395, 27), result.Items[3]);

        var uri = Assert.Single(handler.RequestUris);
        Assert.StartsWith("https://www.googleapis.com/youtube/v3/videos:batchGetStats?", uri);
        Assert.Equal("id,snippet,contentDetails,statistics", QueryValue(uri, "part"));
        Assert.Equal("v1,v2,v3,v4", QueryValue(uri, "id"));
        Assert.Equal("real-key", QueryValue(uri, "key"));
    }

    [Fact]
    public async Task A_successful_statistics_response_without_items_is_still_a_success()
    {
        var (client, _) = CreateClient(Json("""{"items":[]}"""));

        var result = await client.GetVideoStatsAsync(["v1"], CancellationToken.None);

        Assert.Equal(BackfillProviderOutcome.Succeeded, result.Outcome);
        Assert.Empty(result.Items);
    }

    [Fact]
    public async Task More_than_fifty_statistics_ids_are_rejected_before_any_http_call()
    {
        var (client, handler) = CreateClient(Json("{}"));
        var ids = Enumerable.Range(0, 51).Select(index => $"v{index}").ToArray();

        await Assert.ThrowsAsync<ArgumentException>(
            () => client.GetVideoStatsAsync(ids, CancellationToken.None));

        Assert.Empty(handler.RequestUris);
    }

    [Fact]
    public async Task A_quota_exceeded_response_maps_to_the_quota_outcome()
    {
        var (client, _) = CreateClient(Status(
            HttpStatusCode.Forbidden,
            """{"error":{"errors":[{"reason":"quotaExceeded"}]}}"""));

        var page = await client.GetUploadsPageAsync("UU-UC1", null, CancellationToken.None);
        var stats = await client.GetVideoStatsAsync(["v1"], CancellationToken.None);

        Assert.Equal(BackfillProviderOutcome.QuotaExceeded, page.Outcome);
        Assert.Equal(BackfillProviderOutcome.QuotaExceeded, stats.Outcome);
    }

    [Theory]
    [InlineData(HttpStatusCode.InternalServerError)]
    [InlineData(HttpStatusCode.NotFound)]
    public async Task A_failed_provider_response_maps_to_the_fixed_safe_reason(HttpStatusCode statusCode)
    {
        var (client, _) = CreateClient(Status(statusCode, """{"error":{"message":"raw provider text"}}"""));

        var page = await client.GetUploadsPageAsync("UU-UC1", null, CancellationToken.None);
        var stats = await client.GetVideoStatsAsync(["v1"], CancellationToken.None);

        Assert.Equal(BackfillProviderOutcome.Failed, page.Outcome);
        Assert.Equal("Unable to retrieve uploads from YouTube. Try again later.", page.FailureReason);
        Assert.DoesNotContain("raw provider text", page.FailureReason, StringComparison.Ordinal);

        Assert.Equal(BackfillProviderOutcome.Failed, stats.Outcome);
        Assert.Equal(
            "Unable to retrieve video statistics from YouTube. Try again later.",
            stats.FailureReason);
    }

    [Fact]
    public async Task A_placeholder_api_key_never_reaches_the_provider()
    {
        var (client, handler) = CreateClient(Json("{}"), apiKey: "YOUR_API_KEY_HERE");

        var result = await client.GetUploadsPageAsync("UU-UC1", null, CancellationToken.None);

        Assert.Equal(BackfillProviderOutcome.Failed, result.Outcome);
        Assert.Empty(handler.RequestUris);
    }

    [Fact]
    public async Task A_missing_api_key_is_reported_without_an_http_call()
    {
        var (client, handler) = CreateClient(Json("{}"), apiKey: null);

        var result = await client.ResolveUploadsPlaylistAsync("UC1", CancellationToken.None);

        Assert.Equal(BackfillProviderOutcome.Failed, result.Outcome);
        Assert.Equal("Unable to find this channel's uploads playlist.", result.FailureReason);
        Assert.Empty(handler.RequestUris);
    }

    [Fact]
    public async Task A_successful_provider_call_is_not_logged()
    {
        var logSink = new Channels.TestLogSink();
        var handler = new StubHttpMessageHandler(_ => Json("""{"items":[]}""")());
        var client = new YouTubeBackfillClient(
            CreateHttpClient(handler),
            new FakeAppSettingsService("real-key"),
            logSink.CreateLogger<YouTubeBackfillClient>());

        await client.GetUploadsPageAsync("UU-UC1", null, CancellationToken.None);

        // A successful provider response is normal control flow, so it must not grow the local log file.
        Assert.Empty(logSink.Entries);
    }

    [Fact]
    public async Task A_failed_provider_call_is_logged_with_the_endpoint_status_and_outcome_only()
    {
        var logSink = new Channels.TestLogSink();
        var handler = new StubHttpMessageHandler(_ => new HttpResponseMessage(HttpStatusCode.InternalServerError)
        {
            Content = new StringContent("secret-provider-body")
        });
        var client = new YouTubeBackfillClient(
            CreateHttpClient(handler),
            new FakeAppSettingsService("real-key"),
            logSink.CreateLogger<YouTubeBackfillClient>());

        await client.GetUploadsPageAsync("UU-UC1", null, CancellationToken.None);

        var entry = Assert.Single(logSink.Entries);
        Assert.Equal(LogLevel.Warning, entry.Level);
        Assert.StartsWith("YouTubeApiRequestFailed", entry.Message, StringComparison.Ordinal);
        Assert.Equal("playlistItems.list", entry.Properties["YouTubeEndpoint"]);
        Assert.Equal(500, entry.Properties["StatusCode"]);
        Assert.Equal("Failed", entry.Properties["Outcome"]);

        // Only the endpoint, status, and outcome are logged: no raw provider body and no API key.
        var serialized = JsonSerializer.Serialize(entry.Properties);
        Assert.DoesNotContain("secret-provider-body", serialized, StringComparison.Ordinal);
        Assert.DoesNotContain("real-key", serialized, StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_continuation_page_request_sends_the_durable_cursor()
    {
        var handler = new StubHttpMessageHandler(_ => Json("""{"items":[]}""")());
        var client = new YouTubeBackfillClient(
            CreateHttpClient(handler),
            new FakeAppSettingsService("real-key"),
            NullLogger<YouTubeBackfillClient>.Instance);

        await client.GetUploadsPageAsync("UU-UC1", "CURSOR-PAGE-2", CancellationToken.None);

        var requestUri = Assert.Single(handler.RequestUris);
        Assert.Contains("pageToken=CURSOR-PAGE-2", requestUri, StringComparison.Ordinal);
        Assert.Contains("playlistId=UU-UC1", requestUri, StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_first_page_request_omits_the_cursor()
    {
        var handler = new StubHttpMessageHandler(_ => Json("""{"items":[]}""")());
        var client = new YouTubeBackfillClient(
            CreateHttpClient(handler),
            new FakeAppSettingsService("real-key"),
            NullLogger<YouTubeBackfillClient>.Instance);

        await client.GetUploadsPageAsync("UU-UC1", null, CancellationToken.None);

        var requestUri = Assert.Single(handler.RequestUris);
        Assert.DoesNotContain("pageToken=", requestUri, StringComparison.Ordinal);
    }

    private static (YouTubeBackfillClient Client, StubHttpMessageHandler Handler) CreateClient(
        Func<HttpResponseMessage> response,
        string? apiKey = "real-key")
    {
        var handler = new StubHttpMessageHandler(_ => response());

        return (
            new YouTubeBackfillClient(
                CreateHttpClient(handler),
                new FakeAppSettingsService(apiKey),
                NullLogger<YouTubeBackfillClient>.Instance),
            handler);
    }

    private static HttpClient CreateHttpClient(HttpMessageHandler handler) =>
        new(handler)
        {
            BaseAddress = new Uri("https://www.googleapis.com/youtube/v3/")
        };

    private static Func<HttpResponseMessage> Json(string body) => () => new HttpResponseMessage(HttpStatusCode.OK)
    {
        Content = new StringContent(body)
    };

    private static Func<HttpResponseMessage> Status(HttpStatusCode statusCode, string body) =>
        () => new HttpResponseMessage(statusCode)
        {
            Content = new StringContent(body)
        };

    private static string? QueryValue(string uri, string name)
    {
        foreach (var part in new Uri(uri).Query.TrimStart('?').Split('&'))
        {
            var keyValue = part.Split('=', 2);
            if (keyValue.Length == 2 && Uri.UnescapeDataString(keyValue[0]) == name)
            {
                return Uri.UnescapeDataString(keyValue[1]);
            }
        }

        return null;
    }

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
        public string? GetValue(string key) => key switch
        {
            YouTubeConfigurationKeys.ApiKey => apiKey,
            _ => null
        };

        public string GetRequiredValue(string key) =>
            GetValue(key) ?? throw new InvalidOperationException($"Missing {key}.");

        public T GetRequiredSection<T>(string sectionName) where T : class => throw new NotSupportedException();
    }
}
