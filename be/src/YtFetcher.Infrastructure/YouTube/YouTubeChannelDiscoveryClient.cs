using Microsoft.Extensions.Logging;
using System.Globalization;
using System.Net;
using System.Text.Json;
using YtFetcher.Application.Abstractions.Services;
using YtFetcher.Application.Discovery.DTO.Result;
using YtFetcher.Infrastructure.YouTube.DTO.Request;
using YtFetcher.Infrastructure.YouTube.DTO.Response;
using YtFetcher.Infrastructure.Configuration;

namespace YtFetcher.Infrastructure.YouTube;

public sealed class YouTubeChannelDiscoveryClient(
    HttpClient httpClient,
    IAppSettingsService appSettingsService,
    ILogger<YouTubeChannelDiscoveryClient> logger) : IYouTubeChannelDiscoveryGateway
{
    private const int MaxChannelIdsPerRequest = 50;
    private const int MaxSearchResults = 25;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true
    };

    public async Task<YouTubeDiscoverySearchResult> SearchAsync(
        string keyword,
        string? pageToken,
        CancellationToken cancellationToken)
    {
        var apiKey = appSettingsService.GetValue(YouTubeConfigurationKeys.ApiKey);
        if (string.IsNullOrWhiteSpace(apiKey) || IsPlaceholder(apiKey))
        {
            return YouTubeDiscoverySearchResult.Failed("MissingApiKey");
        }

        var searchRequest = new YouTubeSearchListRequest
        {
            Keyword = keyword,
            PageToken = pageToken,
            ApiKey = apiKey
        };

        var (search, searchFailure) = await SendAsync<YouTubeSearchListResponse>(
            "search.list",
            BuildSearchUri(searchRequest),
            cancellationToken);
        if (searchFailure is not null)
        {
            return YouTubeDiscoverySearchResult.Failed(searchFailure);
        }

        var validItems = new List<YouTubeDiscoverySearchItem>();
        var malformedItemsSkipped = 0;

        foreach (var item in search!.Items ?? [])
        {
            if (TryParseSearchItem(item, out var parsed))
            {
                validItems.Add(new YouTubeDiscoverySearchItem(
                    item.Snippet!.ChannelId!,
                    parsed.YouTubeVideoId,
                    parsed.VideoTitle,
                    parsed.VideoThumbnailUrl,
                    parsed.VideoPublishedAt));
            }
            else
            {
                malformedItemsSkipped++;
            }
        }

        if (malformedItemsSkipped > 0)
        {
            logger.LogWarning(
                "ChannelDiscoveryMalformedItemsSkipped {Keyword} {SkippedCount}",
                keyword,
                malformedItemsSkipped);
        }

        return YouTubeDiscoverySearchResult.Completed(validItems, malformedItemsSkipped, search.NextPageToken);
    }

    public async Task<YouTubeChannelMetadataResult> GetChannelMetadataAsync(
        IReadOnlyCollection<string> channelIds,
        CancellationToken cancellationToken)
    {
        var apiKey = appSettingsService.GetValue(YouTubeConfigurationKeys.ApiKey);
        if (string.IsNullOrWhiteSpace(apiKey) || IsPlaceholder(apiKey))
        {
            return YouTubeChannelMetadataResult.Failed("MissingApiKey");
        }

        if (channelIds.Count == 0)
        {
            return YouTubeChannelMetadataResult.Completed([]);
        }

        if (channelIds.Count > MaxChannelIdsPerRequest)
        {
            throw new ArgumentException(
                $"A discovery metadata lookup cannot exceed {MaxChannelIdsPerRequest} channel ids.",
                nameof(channelIds));
        }

        var channelsRequest = new YouTubeChannelsListRequest
        {
            ChannelIds = channelIds.ToArray(),
            ApiKey = apiKey
        };

        var (channels, channelsFailure) = await SendAsync<YouTubeChannelsListResponse>(
            "channels.list",
            BuildChannelsUri(channelsRequest),
            cancellationToken);
        if (channelsFailure is not null)
        {
            return YouTubeChannelMetadataResult.Failed(channelsFailure);
        }

        var metadata = new List<YouTubeChannelMetadata>();
        foreach (var channel in channels!.Items ?? [])
        {
            if (string.IsNullOrWhiteSpace(channel.Id) || string.IsNullOrWhiteSpace(channel.Snippet?.Title))
            {
                continue;
            }

            metadata.Add(new YouTubeChannelMetadata(
                channel.Id,
                channel.Snippet.Title,
                BestChannelThumbnailUrl(channel.Snippet.Thumbnails),
                channel.ContentDetails?.RelatedPlaylists?.Uploads));
        }

        return YouTubeChannelMetadataResult.Completed(metadata);
    }

    private async Task<(TResponse? Response, string? FailureReason)> SendAsync<TResponse>(
        string endpoint,
        string relativeUri,
        CancellationToken cancellationToken)
        where TResponse : class
    {
        try
        {
            using var response = await httpClient.GetAsync(relativeUri, cancellationToken);
            var body = await response.Content.ReadAsStringAsync(cancellationToken);

            if (!response.IsSuccessStatusCode)
            {
                var isQuotaExceeded = response.StatusCode == HttpStatusCode.Forbidden &&
                                      body.Contains("quotaExceeded", StringComparison.OrdinalIgnoreCase);

                return (null, isQuotaExceeded ? "QuotaExceeded" : "HttpError");
            }

            var parsed = JsonSerializer.Deserialize<TResponse>(body, JsonOptions);

            return parsed is null ? (null, "MalformedResponse") : (parsed, null);
        }
        catch (TaskCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            return (null, "Timeout");
        }
        catch (HttpRequestException)
        {
            return (null, "NetworkError");
        }
        catch (JsonException)
        {
            return (null, "MalformedResponse");
        }
    }

    private static string BuildSearchUri(YouTubeSearchListRequest request)
    {
        var parameters = new List<string>
        {
            "part=snippet",
            "type=video",
            "videoDuration=short",
            "order=date",
            $"maxResults={MaxSearchResults}",
            $"q={Uri.EscapeDataString(request.Keyword)}"
        };

        if (!string.IsNullOrWhiteSpace(request.PageToken))
        {
            parameters.Add($"pageToken={Uri.EscapeDataString(request.PageToken)}");
        }

        parameters.Add($"key={Uri.EscapeDataString(request.ApiKey)}");

        return "search?" + string.Join('&', parameters);
    }

    private static string BuildChannelsUri(YouTubeChannelsListRequest request) =>
        "channels?part=snippet,contentDetails" +
        $"&id={string.Join(',', request.ChannelIds)}" +
        $"&key={Uri.EscapeDataString(request.ApiKey)}";

    private static bool TryParseSearchItem(SearchItem item, out ParsedSearchItem parsed)
    {
        parsed = default;

        if (string.IsNullOrWhiteSpace(item.Id?.VideoId) ||
            string.IsNullOrWhiteSpace(item.Snippet?.ChannelId) ||
            string.IsNullOrWhiteSpace(item.Snippet?.Title))
        {
            return false;
        }

        if (!DateTimeOffset.TryParse(
                item.Snippet.PublishedAt,
                CultureInfo.InvariantCulture,
                DateTimeStyles.AdjustToUniversal | DateTimeStyles.AssumeUniversal,
                out var publishedAt))
        {
            return false;
        }

        parsed = new ParsedSearchItem(
            item.Id!.VideoId!,
            item.Snippet!.Title!,
            BestVideoThumbnailUrl(item.Snippet.Thumbnails),
            publishedAt);

        return true;
    }

    private static string? BestChannelThumbnailUrl(Thumbnails? thumbnails) =>
        thumbnails?.Default?.Url ?? thumbnails?.Medium?.Url ?? thumbnails?.High?.Url;

    private static string? BestVideoThumbnailUrl(Thumbnails? thumbnails) =>
        thumbnails?.High?.Url ?? thumbnails?.Medium?.Url ?? thumbnails?.Default?.Url;

    private static bool IsPlaceholder(string apiKey)
    {
        var value = apiKey.Trim();

        return value.Contains("PLACEHOLDER", StringComparison.OrdinalIgnoreCase) ||
               value.Contains("CHANGE_ME", StringComparison.OrdinalIgnoreCase) ||
               value.Contains("CHANGEME", StringComparison.OrdinalIgnoreCase) ||
               value.StartsWith("PUT_", StringComparison.OrdinalIgnoreCase) ||
               value.StartsWith("YOUR_", StringComparison.OrdinalIgnoreCase);
    }

    private readonly record struct ParsedSearchItem(
        string YouTubeVideoId,
        string VideoTitle,
        string? VideoThumbnailUrl,
        DateTimeOffset VideoPublishedAt);

    private sealed record ChannelMetadata(
        string YouTubeChannelId,
        string ChannelName,
        string? ChannelThumbnailUrl,
        string? UploadsPlaylistId);
}
