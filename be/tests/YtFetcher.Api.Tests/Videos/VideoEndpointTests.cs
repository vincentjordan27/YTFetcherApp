using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using YtFetcher.Api.Tests.Channels;
using YtFetcher.Core.Channels;
using YtFetcher.Core.Videos;
using YtFetcher.Infrastructure.Persistence;

namespace YtFetcher.Api.Tests.Videos;

/// <summary>
/// The active-video list endpoint: transport validation, the body-based route, and the JSON shape the
/// frontend consumes.
/// </summary>
[Collection(ApiStartupCollection.Name)]
public class VideoEndpointTests
{
    private static readonly DateTimeOffset Now = new(2026, 9, 30, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task Active_list_returns_the_documented_json_shape()
    {
        using var factory = new ChannelApiFactory();
        using var hostConfiguration = new HostConfigurationScope();
        using var application = factory.WithWebHostBuilder(_ => { });
        var videoId = await SeedVideoAsync(application, "Sakura Channel", "v1", "Sakura festival", views: 4_200);
        using var client = application.CreateClient();

        var response = await client.PostAsJsonAsync(
            "/api/videos/active/list",
            new { page = 1, pageSize = 10, searchText = (string?)null });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var root = document.RootElement;
        Assert.Equal(1, root.GetProperty("page").GetInt32());
        Assert.Equal(10, root.GetProperty("pageSize").GetInt32());
        Assert.Equal(1, root.GetProperty("totalCount").GetInt32());

        var item = Assert.Single(root.GetProperty("items").EnumerateArray().ToList());
        Assert.Equal(videoId, item.GetProperty("id").GetGuid());
        Assert.Equal("v1", item.GetProperty("youtubeVideoId").GetString());
        Assert.Equal("Sakura festival", item.GetProperty("title").GetString());
        Assert.Equal("Sakura Channel", item.GetProperty("channelName").GetString());
        Assert.Equal("https://example.com/v1.jpg", item.GetProperty("thumbnailUrl").GetString());
        Assert.Equal(120_000, item.GetProperty("durationMillis").GetInt64());
        Assert.Equal(4_200, item.GetProperty("currentViews").GetInt64());
        Assert.Equal(310, item.GetProperty("currentLikes").GetInt64());
        Assert.NotEqual(default, item.GetProperty("publishedAt").GetDateTimeOffset());
        Assert.NotEqual(default, item.GetProperty("lastStatsFetchedAt").GetDateTimeOffset());
    }

    [Fact]
    public async Task Active_list_filters_by_video_title_or_channel_name()
    {
        using var factory = new ChannelApiFactory();
        using var hostConfiguration = new HostConfigurationScope();
        using var application = factory.WithWebHostBuilder(_ => { });
        await SeedVideoAsync(application, "Kyoto Walks", "v-title", "Sakura festival");
        await SeedVideoAsync(application, "Sakura Channel", "v-channel", "Morning walk");
        await SeedVideoAsync(application, "Other", "v-other", "Unrelated");
        using var client = application.CreateClient();

        var response = await client.PostAsJsonAsync(
            "/api/videos/active/list",
            new { page = 1, pageSize = 10, searchText = "sakura" });

        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.Equal(2, document.RootElement.GetProperty("totalCount").GetInt32());
    }

    [Fact]
    public async Task Active_list_without_matches_returns_an_empty_page()
    {
        using var factory = new ChannelApiFactory();
        using var hostConfiguration = new HostConfigurationScope();
        using var application = factory.WithWebHostBuilder(_ => { });
        using var client = application.CreateClient();

        var response = await client.PostAsJsonAsync(
            "/api/videos/active/list",
            new { page = 1, pageSize = 10, searchText = (string?)null });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.Empty(document.RootElement.GetProperty("items").EnumerateArray().ToList());
        Assert.Equal(0, document.RootElement.GetProperty("totalCount").GetInt32());
    }

    [Theory]
    [InlineData(0, 10, "page")]
    [InlineData(1, 0, "pageSize")]
    [InlineData(1, 101, "pageSize")]
    public async Task Active_list_rejects_invalid_paging_with_the_established_field_keys(
        int page,
        int pageSize,
        string expectedKey)
    {
        using var factory = new ChannelApiFactory();
        using var hostConfiguration = new HostConfigurationScope();
        using var application = factory.WithWebHostBuilder(_ => { });
        using var client = application.CreateClient();

        var response = await client.PostAsJsonAsync(
            "/api/videos/active/list",
            new { page, pageSize, searchText = (string?)null });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);

        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.True(document.RootElement.GetProperty("errors").TryGetProperty(expectedKey, out _));
    }

    private static async Task<Guid> SeedVideoAsync(
        WebApplicationFactory<Program> application,
        string channelName,
        string youtubeVideoId,
        string title,
        long views = 100)
    {
        using var scope = application.Services.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<YtFetcherDbContext>();

        var channel = new Channel
        {
            Id = Guid.NewGuid(),
            YoutubeChannelId = $"UC-{Guid.NewGuid():N}",
            Name = channelName,
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
            Title = title,
            ThumbnailUrl = $"https://example.com/{youtubeVideoId}.jpg",
            PublishedAt = Now.AddDays(-20),
            DurationMillis = 120_000,
            CurrentViews = views,
            CurrentLikes = 310,
            TrackingStatus = VideoTrackingStatus.ACTIVE,
            LastStatsFetchedAt = Now,
            IsDeleted = false,
            CreatedAt = Now,
            ModifiedAt = Now
        };

        context.Channels.Add(channel);
        context.Videos.Add(video);
        await context.SaveChangesAsync();

        return video.Id;
    }
}
