using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using YtFetcher.Api.Tests.Channels;
using YtFetcher.Core.Channels;
using YtFetcher.Core.Videos;
using YtFetcher.Infrastructure.Persistence;

namespace YtFetcher.Api.Tests.Videos;

/// <summary>
/// The on-demand per-video history endpoint and the narrow active-video count endpoint: transport
/// validation, 404, deterministic paging, soft-delete exclusion, and the JSON shape the frontend consumes.
/// </summary>
[Collection(ApiStartupCollection.Name)]
public class VideoHistoryEndpointTests
{
    private static readonly DateTimeOffset Now = new(2026, 9, 30, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task History_returns_the_documented_json_shape_newest_capture_first()
    {
        using var factory = new ChannelApiFactory();
        using var hostConfiguration = new HostConfigurationScope();
        using var application = factory.WithWebHostBuilder(_ => { });
        var videoId = await SeedVideoAsync(application, "v-history");
        await SeedSnapshotAsync(application, videoId, capturedAt: Now.AddHours(-2), views: 1_000, likes: 20);
        await SeedSnapshotAsync(application, videoId, capturedAt: Now.AddHours(-1), views: 2_500, likes: 55);
        using var client = application.CreateClient();

        var response = await client.PostAsJsonAsync(
            $"/api/videos/{videoId}/history/list",
            new { page = 1, pageSize = 10 });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var root = document.RootElement;
        Assert.Equal(1, root.GetProperty("page").GetInt32());
        Assert.Equal(10, root.GetProperty("pageSize").GetInt32());
        Assert.Equal(2, root.GetProperty("totalCount").GetInt32());

        var items = root.GetProperty("items").EnumerateArray().ToList();
        Assert.Equal(2, items.Count);

        Assert.Equal(Now.AddHours(-1), items[0].GetProperty("capturedAt").GetDateTimeOffset());
        Assert.Equal(2_500, items[0].GetProperty("viewCount").GetInt64());
        Assert.Equal(55, items[0].GetProperty("likeCount").GetInt64());

        Assert.Equal(Now.AddHours(-2), items[1].GetProperty("capturedAt").GetDateTimeOffset());
        Assert.Equal(1_000, items[1].GetProperty("viewCount").GetInt64());
    }

    [Fact]
    public async Task History_returns_404_for_a_missing_video()
    {
        using var factory = new ChannelApiFactory();
        using var hostConfiguration = new HostConfigurationScope();
        using var application = factory.WithWebHostBuilder(_ => { });
        using var client = application.CreateClient();

        var response = await client.PostAsJsonAsync(
            $"/api/videos/{Guid.NewGuid()}/history/list",
            new { page = 1, pageSize = 10 });

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task History_returns_404_for_a_soft_deleted_video()
    {
        using var factory = new ChannelApiFactory();
        using var hostConfiguration = new HostConfigurationScope();
        using var application = factory.WithWebHostBuilder(_ => { });
        var videoId = await SeedVideoAsync(application, "v-deleted", deleted: true);
        using var client = application.CreateClient();

        var response = await client.PostAsJsonAsync(
            $"/api/videos/{videoId}/history/list",
            new { page = 1, pageSize = 10 });

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Theory]
    [InlineData(0, 10, "page")]
    [InlineData(1, 0, "pageSize")]
    [InlineData(1, 101, "pageSize")]
    public async Task History_rejects_invalid_paging_with_the_established_field_keys(
        int page,
        int pageSize,
        string expectedKey)
    {
        using var factory = new ChannelApiFactory();
        using var hostConfiguration = new HostConfigurationScope();
        using var application = factory.WithWebHostBuilder(_ => { });
        var videoId = await SeedVideoAsync(application, "v-page");
        using var client = application.CreateClient();

        var response = await client.PostAsJsonAsync(
            $"/api/videos/{videoId}/history/list",
            new { page, pageSize });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);

        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.True(document.RootElement.GetProperty("errors").TryGetProperty(expectedKey, out _));
    }

    [Fact]
    public async Task History_rejects_a_malformed_video_id()
    {
        using var factory = new ChannelApiFactory();
        using var hostConfiguration = new HostConfigurationScope();
        using var application = factory.WithWebHostBuilder(_ => { });
        using var client = application.CreateClient();

        var response = await client.PostAsJsonAsync(
            "/api/videos/not-a-guid/history/list",
            new { page = 1, pageSize = 10 });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);

        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.True(document.RootElement.GetProperty("errors").TryGetProperty("videoId", out _));
    }

    [Fact]
    public async Task Active_count_excludes_reference_stopped_and_deleted_rows()
    {
        using var factory = new ChannelApiFactory();
        using var hostConfiguration = new HostConfigurationScope();
        using var application = factory.WithWebHostBuilder(_ => { });
        await SeedVideoAsync(application, "v-active-1");
        await SeedVideoAsync(application, "v-active-2");
        await SeedVideoAsync(application, "v-reference", isReference: true);
        await SeedVideoAsync(application, "v-stopped", stopped: true);
        await SeedVideoAsync(application, "v-deleted", deleted: true);
        using var client = application.CreateClient();

        var response = await client.GetAsync("/api/videos/active/count");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.Equal(2, document.RootElement.GetProperty("count").GetInt32());
    }

    private static async Task<Guid> SeedVideoAsync(
        WebApplicationFactory<Program> application,
        string youtubeVideoId,
        bool isReference = false,
        bool stopped = false,
        bool deleted = false)
    {
        using var scope = application.Services.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<YtFetcherDbContext>();

        var channel = new Channel
        {
            Id = Guid.NewGuid(),
            YoutubeChannelId = $"UC-{Guid.NewGuid():N}",
            Name = "Sakura Channel",
            Status = ChannelStatus.APPROVED,
            ApprovedAt = Now,
            BackfillStatus = BackfillStatus.COMPLETED,
            IsDeleted = false,
            DiscoveredAt = Now,
            CreatedAt = Now,
            ModifiedAt = Now
        };

        var video = new Video
        {
            Id = Guid.NewGuid(),
            YoutubeVideoId = youtubeVideoId,
            ChannelId = channel.Id,
            Title = $"Title {youtubeVideoId}",
            ThumbnailUrl = null,
            PublishedAt = Now.AddDays(-20),
            DurationMillis = 120_000,
            CurrentViews = 500,
            CurrentLikes = 20,
            IsReference = isReference,
            TrackingStatus = isReference || stopped ? VideoTrackingStatus.STOPPED : VideoTrackingStatus.ACTIVE,
            LastStatsFetchedAt = Now,
            IsDeleted = deleted,
            CreatedAt = Now,
            ModifiedAt = Now
        };

        context.Channels.Add(channel);
        context.Videos.Add(video);
        await context.SaveChangesAsync();

        return video.Id;
    }

    private static async Task SeedSnapshotAsync(
        WebApplicationFactory<Program> application,
        Guid videoId,
        DateTimeOffset capturedAt,
        long views,
        long likes)
    {
        using var scope = application.Services.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<YtFetcherDbContext>();

        context.VideoSnapshots.Add(new VideoSnapshot
        {
            VideoId = videoId,
            CapturedAt = capturedAt,
            ViewCount = views,
            LikeCount = likes,
            IsDeleted = false,
            CreatedAt = capturedAt,
            ModifiedAt = capturedAt
        });

        await context.SaveChangesAsync();
    }
}
