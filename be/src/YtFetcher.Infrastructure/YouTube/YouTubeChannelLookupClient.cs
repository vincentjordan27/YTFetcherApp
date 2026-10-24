using System.Net;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using YtFetcher.Application.Abstractions.Services;
using YtFetcher.Application.Channels.DTO.Result;
using YtFetcher.Infrastructure.YouTube.DTO.Request;
using YtFetcher.Infrastructure.YouTube.DTO.Response;
using YtFetcher.Infrastructure.Configuration;

namespace YtFetcher.Infrastructure.YouTube;

/// <summary>
/// YouTube adapter for the manual-add handle lookup. It makes exactly one <c>channels.list</c>
/// request with the <c>forHandle</c> filter, maps the raw JSON into the normalized Application
/// result, and never emits an API key, a raw provider body, or provider error text.
/// </summary>
public sealed class YouTubeChannelLookupClient(
    HttpClient httpClient,
    IAppSettingsService appSettingsService,
    ILogger<YouTubeChannelLookupClient> logger) : IYouTubeChannelLookupGateway
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true
    };

    public async Task<ManualChannelLookupResult> LookupByHandleAsync(
        string handle,
        CancellationToken cancellationToken)
    {
        var apiKey = appSettingsService.GetValue(YouTubeConfigurationKeys.ApiKey);
        if (string.IsNullOrWhiteSpace(apiKey) || IsPlaceholder(apiKey))
        {
            return ManualChannelLookupResult.Unavailable();
        }

        var request = new YouTubeChannelHandleLookupRequest
        {
            Handle = handle,
            ApiKey = apiKey
        };

        try
        {
            using var response = await httpClient.GetAsync(BuildChannelsUri(request), cancellationToken);

            if (!response.IsSuccessStatusCode)
            {
                // Safe structured diagnostics only: endpoint and status, never the body or the key.
                logger.LogWarning(
                    "YouTubeChannelHandleLookupFailed {YouTubeEndpoint} {StatusCode}",
                    "channels.list",
                    (int)response.StatusCode);

                return ManualChannelLookupResult.Unavailable();
            }

            var body = await response.Content.ReadAsStringAsync(cancellationToken);
            var parsed = JsonSerializer.Deserialize<YouTubeChannelsListResponse>(body, JsonOptions);
            var item = parsed?.Items?.FirstOrDefault();

            if (item is null || string.IsNullOrWhiteSpace(item.Id) || string.IsNullOrWhiteSpace(item.Snippet?.Title))
            {
                return ManualChannelLookupResult.NotFound();
            }

            return ManualChannelLookupResult.Found(
                item.Id,
                item.Snippet.Title,
                BestThumbnailUrl(item.Snippet.Thumbnails),
                item.ContentDetails?.RelatedPlaylists?.Uploads);
        }
        catch (TaskCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            return ManualChannelLookupResult.Unavailable();
        }
        catch (HttpRequestException)
        {
            return ManualChannelLookupResult.Unavailable();
        }
        catch (JsonException)
        {
            return ManualChannelLookupResult.Unavailable();
        }
    }

    private static string BuildChannelsUri(YouTubeChannelHandleLookupRequest request) =>
        "channels?part=snippet,contentDetails" +
        $"&forHandle={Uri.EscapeDataString(request.Handle)}" +
        $"&key={Uri.EscapeDataString(request.ApiKey)}";

    private static string? BestThumbnailUrl(Thumbnails? thumbnails) =>
        thumbnails?.Default?.Url ?? thumbnails?.Medium?.Url ?? thumbnails?.High?.Url;

    private static bool IsPlaceholder(string apiKey)
    {
        var value = apiKey.Trim();

        return value.Contains("PLACEHOLDER", StringComparison.OrdinalIgnoreCase) ||
               value.Contains("CHANGE_ME", StringComparison.OrdinalIgnoreCase) ||
               value.Contains("CHANGEME", StringComparison.OrdinalIgnoreCase) ||
               value.StartsWith("PUT_", StringComparison.OrdinalIgnoreCase) ||
               value.StartsWith("YOUR_", StringComparison.OrdinalIgnoreCase);
    }
}
