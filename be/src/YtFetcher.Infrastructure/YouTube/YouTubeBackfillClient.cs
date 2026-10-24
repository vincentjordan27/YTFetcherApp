using System.Globalization;
using System.Net;
using System.Text.Json;
using System.Xml;
using Microsoft.Extensions.Logging;
using YtFetcher.Application.Abstractions.Services;
using YtFetcher.Application.Backfill.DTO.Result;
using YtFetcher.Infrastructure.YouTube.DTO.Request;
using YtFetcher.Infrastructure.YouTube.DTO.Response;
using YtFetcher.Infrastructure.Configuration;

namespace YtFetcher.Infrastructure.YouTube;

/// <summary>
/// YouTube adapter for the approved-channel backfill. It owns the raw provider shapes, normalizes
/// them into Application results, and never emits an API key, a raw provider body, or provider error
/// text outside this class.
/// </summary>
public sealed class YouTubeBackfillClient(
    HttpClient httpClient,
    IAppSettingsService appSettingsService,
    ILogger<YouTubeBackfillClient> logger) : IYouTubeBackfillGateway
{
    private const int MaxPlaylistItemsPerPage = 50;

    /// <summary>Google <c>videos.list</c> contract bound; distinct from the playlistItems page size.</summary>
    private const int MaxVideoIdsPerRequest = 50;

    private const string UploadsPlaylistUnavailableReason = "Unable to find this channel's uploads playlist.";

    private const string UploadsPageFailedReason = "Unable to retrieve uploads from YouTube. Try again later.";

    private const string VideoStatsFailedReason =
        "Unable to retrieve video statistics from YouTube. Try again later.";

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true
    };

    public async Task<UploadsPlaylistResult> ResolveUploadsPlaylistAsync(
        string youtubeChannelId,
        CancellationToken cancellationToken)
    {
        var apiKey = ReadApiKey();
        if (apiKey is null)
        {
            return UploadsPlaylistResult.Failed(UploadsPlaylistUnavailableReason);
        }

        var request = new YouTubeChannelsListRequest
        {
            ChannelIds = [youtubeChannelId],
            ApiKey = apiKey
        };

        var (response, outcome) = await SendAsync<YouTubeChannelsListResponse>(
            "channels.list",
            "channels?part=contentDetails" +
            $"&id={Uri.EscapeDataString(request.ChannelIds[0])}" +
            $"&key={Uri.EscapeDataString(request.ApiKey)}",
            cancellationToken);

        if (outcome == BackfillProviderOutcome.QuotaExceeded)
        {
            return UploadsPlaylistResult.QuotaExceeded();
        }

        if (outcome != BackfillProviderOutcome.Succeeded || response is null)
        {
            return UploadsPlaylistResult.Failed(UploadsPlaylistUnavailableReason);
        }

        var uploadsPlaylistId = response.Items?
            .Select(item => item.ContentDetails?.RelatedPlaylists?.Uploads)
            .FirstOrDefault(uploads => !string.IsNullOrWhiteSpace(uploads));

        return string.IsNullOrWhiteSpace(uploadsPlaylistId)
            ? UploadsPlaylistResult.NotFound(UploadsPlaylistUnavailableReason)
            : UploadsPlaylistResult.Succeeded(uploadsPlaylistId);
    }

    public async Task<UploadsPageResult> GetUploadsPageAsync(
        string uploadsPlaylistId,
        string? pageToken,
        CancellationToken cancellationToken)
    {
        var apiKey = ReadApiKey();
        if (apiKey is null)
        {
            return UploadsPageResult.Failed(UploadsPageFailedReason);
        }

        var request = new YouTubePlaylistItemsListRequest
        {
            UploadsPlaylistId = uploadsPlaylistId,
            MaxResults = MaxPlaylistItemsPerPage,
            ApiKey = apiKey,
            PageToken = pageToken
        };

        // The cursor is only appended when it exists, so a first-page request keeps its original shape.
        var pageTokenQuery = string.IsNullOrWhiteSpace(request.PageToken)
            ? string.Empty
            : $"&pageToken={Uri.EscapeDataString(request.PageToken)}";

        var (response, outcome) = await SendAsync<YouTubePlaylistItemsListResponse>(
            "playlistItems.list",
            "playlistItems?part=snippet,contentDetails" +
            $"&playlistId={Uri.EscapeDataString(request.UploadsPlaylistId)}" +
            $"&maxResults={request.MaxResults}" +
            pageTokenQuery +
            $"&key={Uri.EscapeDataString(request.ApiKey)}",
            cancellationToken);

        if (outcome == BackfillProviderOutcome.QuotaExceeded)
        {
            return UploadsPageResult.QuotaExceeded();
        }

        if (outcome != BackfillProviderOutcome.Succeeded || response is null)
        {
            return UploadsPageResult.Failed(UploadsPageFailedReason);
        }

        var items = new List<UploadsPageItemResult>();
        foreach (var item in response.Items ?? [])
        {
            items.Add(new UploadsPageItemResult(
                item.ContentDetails?.VideoId ?? string.Empty,
                item.Snippet?.Title ?? string.Empty,
                BestVideoThumbnailUrl(item.Snippet?.Thumbnails),
                ParsePublishedAt(item.Snippet?.PublishedAt)));
        }

        return UploadsPageResult.Succeeded(items, response.NextPageToken);
    }

    public async Task<VideoStatsBatchResult> GetVideoStatsAsync(
        IReadOnlyCollection<string> youtubeVideoIds,
        CancellationToken cancellationToken)
    {
        var apiKey = ReadApiKey();
        if (apiKey is null)
        {
            return VideoStatsBatchResult.Failed(VideoStatsFailedReason);
        }

        if (youtubeVideoIds.Count > MaxVideoIdsPerRequest)
        {
            throw new ArgumentException(
                $"A statistics batch cannot exceed {MaxVideoIdsPerRequest} video ids.",
                nameof(youtubeVideoIds));
        }

        var request = new YouTubeVideoStatsBatchRequest
        {
            VideoIds = youtubeVideoIds
                .Where(videoId => !string.IsNullOrWhiteSpace(videoId))
                .ToArray(),
            ApiKey = apiKey
        };

        var (response, outcome) = await SendAsync<YouTubeVideoStatsBatchResponse>(
            "videos.batchGetStats",
            // The dot segment keeps the colon in the first path segment from being read as a scheme.
            "./videos:batchGetStats?part=id,snippet,contentDetails,statistics" +
            $"&id={string.Join(',', request.VideoIds.Select(Uri.EscapeDataString))}" +
            $"&key={Uri.EscapeDataString(request.ApiKey)}",
            cancellationToken);

        if (outcome == BackfillProviderOutcome.QuotaExceeded)
        {
            return VideoStatsBatchResult.QuotaExceeded();
        }

        if (outcome != BackfillProviderOutcome.Succeeded || response is null)
        {
            return VideoStatsBatchResult.Failed(VideoStatsFailedReason);
        }

        var items = new List<VideoStatsItemResult>();
        foreach (var item in response.Items ?? [])
        {
            if (string.IsNullOrWhiteSpace(item.Id))
            {
                continue;
            }

            // Absent public numeric values are normalized to zero; a missing duration stays invalid.
            items.Add(new VideoStatsItemResult(
                item.Id,
                ReadDurationMillis(item.ContentDetails),
                Math.Max(0, ReadInt64(item.Statistics?.ViewCount, fallback: 0)),
                Math.Max(0, ReadInt64(item.Statistics?.LikeCount, fallback: 0))));
        }

        return VideoStatsBatchResult.Succeeded(items);
    }

    private async Task<(TResponse? Response, BackfillProviderOutcome Outcome)> SendAsync<TResponse>(
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

                // Only a failure is an operator event; a successful response is normal control flow. The
                // endpoint and status are safe fields, the raw provider body never is.
                logger.LogWarning(
                    "YouTubeApiRequestFailed {YouTubeEndpoint} {StatusCode} {Outcome}",
                    endpoint,
                    (int)response.StatusCode,
                    isQuotaExceeded ? "QuotaExceeded" : "Failed");

                return (null, isQuotaExceeded
                    ? BackfillProviderOutcome.QuotaExceeded
                    : BackfillProviderOutcome.Failed);
            }

            var parsed = JsonSerializer.Deserialize<TResponse>(body, JsonOptions);

            return parsed is null
                ? (null, BackfillProviderOutcome.Failed)
                : (parsed, BackfillProviderOutcome.Succeeded);
        }
        catch (TaskCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            return (null, BackfillProviderOutcome.Failed);
        }
        catch (HttpRequestException)
        {
            return (null, BackfillProviderOutcome.Failed);
        }
        catch (JsonException)
        {
            return (null, BackfillProviderOutcome.Failed);
        }
    }

    private string? ReadApiKey()
    {
        var apiKey = appSettingsService.GetValue(YouTubeConfigurationKeys.ApiKey);

        return string.IsNullOrWhiteSpace(apiKey) || IsPlaceholder(apiKey) ? null : apiKey;
    }

    private static DateTimeOffset? ParsePublishedAt(string? publishedAt) =>
        DateTimeOffset.TryParse(
            publishedAt,
            CultureInfo.InvariantCulture,
            DateTimeStyles.AdjustToUniversal | DateTimeStyles.AssumeUniversal,
            out var parsed)
            ? parsed
            : null;

    private static string? BestVideoThumbnailUrl(Thumbnails? thumbnails) =>
        thumbnails?.High?.Url ?? thumbnails?.Medium?.Url ?? thumbnails?.Default?.Url;

    private static long ReadInt64(JsonElement? element, long fallback)
    {
        if (element is not { } value)
        {
            return fallback;
        }

        return value.ValueKind switch
        {
            JsonValueKind.Number when value.TryGetInt64(out var number) => number,
            JsonValueKind.String when long.TryParse(
                value.GetString(),
                NumberStyles.Integer,
                CultureInfo.InvariantCulture,
                out var text) => text,
            _ => fallback
        };
    }

    private static long ReadDurationMillis(VideoStatsContentDetails? contentDetails)
    {
        var durationMillis = ReadInt64(contentDetails?.DurationMillis, fallback: -1);
        if (durationMillis >= 0)
        {
            return durationMillis;
        }

        var duration = contentDetails?.Duration;
        if (string.IsNullOrWhiteSpace(duration))
        {
            return -1;
        }

        if (duration.EndsWith('s') && long.TryParse(
                duration[..^1],
                NumberStyles.Integer,
                CultureInfo.InvariantCulture,
                out var seconds))
        {
            try
            {
                return checked(seconds * 1_000);
            }
            catch (OverflowException)
            {
                return -1;
            }
        }

        try
        {
            var isoDuration = XmlConvert.ToTimeSpan(duration);
            return isoDuration < TimeSpan.Zero
                ? -1
                : isoDuration.Ticks / TimeSpan.TicksPerMillisecond;
        }
        catch (FormatException)
        {
            return -1;
        }
        catch (OverflowException)
        {
            return -1;
        }
    }

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
