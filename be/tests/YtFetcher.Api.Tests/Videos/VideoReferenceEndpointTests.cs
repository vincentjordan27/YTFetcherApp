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
/// The mark-reference action and the reference list endpoint: validation, 204/409 behavior, and the
/// reference JSON shape.
/// </summary>
[Collection(ApiStartupCollection.Name)]
public class VideoReferenceEndpointTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 1, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task Mark_reference_returns_204_and_stops_the_selected_videos()
    {
        using var factory = new ChannelApiFactory();
        using var hostConfiguration = new HostConfigurationScope();
        using var application = factory.WithWebHostBuilder(_ => { });
        var first = await SeedVideoAsync(application, "v-1", "First");
        var second = await SeedVideoAsync(application, "v-2", "Second");
        using var client = application.CreateClient();

        var response = await client.PostAsJsonAsync(
            "/api/videos/mark-reference",
            new { videoIds = new[] { first, second } });

        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);

        var stored = await ReadVideosAsync(application, first, second);
        Assert.All(stored, video =>
        {
            Assert.True(video.IsReference);
            Assert.Equal(VideoTrackingStatus.STOPPED, video.TrackingStatus);
        });
    }

    [Fact]
    public async Task Mark_reference_returns_the_exact_conflict_message_and_changes_nothing()
    {
        using var factory = new ChannelApiFactory();
        using var hostConfiguration = new HostConfigurationScope();
        using var application = factory.WithWebHostBuilder(_ => { });
        var active = await SeedVideoAsync(application, "v-active", "Active");
        var stopped = await SeedVideoAsync(application, "v-stopped", "Stopped", trackingStatus: VideoTrackingStatus.STOPPED);
        using var client = application.CreateClient();

        var response = await client.PostAsJsonAsync(
            "/api/videos/mark-reference",
            new { videoIds = new[] { active, stopped } });

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.Equal(
            "One or more selected videos are no longer active. Refresh the list and try again.",
            document.RootElement.GetProperty("detail").GetString());

        var stored = await ReadVideosAsync(application, active);
        Assert.False(Assert.Single(stored).IsReference);
    }

    [Fact]
    public async Task Mark_reference_rejects_a_missing_selection()
    {
        using var factory = new ChannelApiFactory();
        using var hostConfiguration = new HostConfigurationScope();
        using var application = factory.WithWebHostBuilder(_ => { });
        using var client = application.CreateClient();

        var response = await client.PostAsJsonAsync("/api/videos/mark-reference", new { });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.True(document.RootElement.GetProperty("errors").TryGetProperty("videoIds", out _));
    }

    [Fact]
    public async Task Mark_reference_rejects_an_empty_or_over_bounded_or_duplicated_selection()
    {
        using var factory = new ChannelApiFactory();
        using var hostConfiguration = new HostConfigurationScope();
        using var application = factory.WithWebHostBuilder(_ => { });
        using var client = application.CreateClient();
        var id = Guid.NewGuid();

        await AssertVideoIdsProblemAsync(client, new { videoIds = Array.Empty<Guid>() });
        await AssertVideoIdsProblemAsync(
            client,
            new { videoIds = Enumerable.Range(0, 101).Select(_ => Guid.NewGuid()).ToArray() });
        await AssertVideoIdsProblemAsync(client, new { videoIds = new[] { id, id } });
    }

    [Fact]
    public async Task Reference_list_returns_the_documented_json_shape_for_reference_rows_only()
    {
        using var factory = new ChannelApiFactory();
        using var hostConfiguration = new HostConfigurationScope();
        using var application = factory.WithWebHostBuilder(_ => { });
        var reference = await SeedVideoAsync(application, "v-reference", "Saved clip", isReference: true);
        await SeedVideoAsync(application, "v-active", "Active clip");
        using var client = application.CreateClient();

        var response = await client.PostAsJsonAsync(
            "/api/videos/reference/list",
            new { page = 1, pageSize = 10, searchText = (string?)null });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var root = document.RootElement;

        Assert.Equal(1, root.GetProperty("page").GetInt32());
        Assert.Equal(10, root.GetProperty("pageSize").GetInt32());
        Assert.Equal(1, root.GetProperty("totalCount").GetInt32());

        var item = Assert.Single(root.GetProperty("items").EnumerateArray().ToList());
        Assert.Equal(reference, item.GetProperty("id").GetGuid());
        Assert.Equal("v-reference", item.GetProperty("youtubeVideoId").GetString());
        Assert.Equal("Saved clip", item.GetProperty("title").GetString());
        Assert.Equal("Reference Channel", item.GetProperty("channelName").GetString());
        Assert.Equal("https://example.com/v-reference.jpg", item.GetProperty("thumbnailUrl").GetString());
        Assert.Equal(120_000, item.GetProperty("durationMillis").GetInt64());
        Assert.Equal(4_200, item.GetProperty("currentViews").GetInt64());
        Assert.Equal(310, item.GetProperty("currentLikes").GetInt64());
        Assert.NotEqual(default, item.GetProperty("publishedAt").GetDateTimeOffset());
        Assert.NotEqual(default, item.GetProperty("lastStatsFetchedAt").GetDateTimeOffset());
    }

    [Fact]
    public async Task Reference_list_rejects_an_invalid_page()
    {
        using var factory = new ChannelApiFactory();
        using var hostConfiguration = new HostConfigurationScope();
        using var application = factory.WithWebHostBuilder(_ => { });
        using var client = application.CreateClient();

        var response = await client.PostAsJsonAsync(
            "/api/videos/reference/list",
            new { page = 0, pageSize = 10, searchText = (string?)null });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.True(document.RootElement.GetProperty("errors").TryGetProperty("page", out _));
    }

    [Fact]
    public async Task Reference_list_item_includes_the_provider_channel_id_from_the_joined_channel()
    {
        using var factory = new ChannelApiFactory();
        using var hostConfiguration = new HostConfigurationScope();
        using var application = factory.WithWebHostBuilder(_ => { });
        await SeedVideoAsync(application, "v-reference", "Saved clip", isReference: true);
        using var client = application.CreateClient();

        var response = await client.PostAsJsonAsync(
            "/api/videos/reference/list",
            new { page = 1, pageSize = 10, searchText = (string?)null, sortBy = (string?)null, sortDirection = (string?)null });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var item = Assert.Single(document.RootElement.GetProperty("items").EnumerateArray().ToList());

        // The client builds the channel link from this id, never from the display name.
        Assert.Equal("UC-v-reference", item.GetProperty("youtubeChannelId").GetString());
    }

    [Fact]
    public async Task Unmark_reference_returns_204_and_returns_a_young_video_to_active()
    {
        using var factory = new ChannelApiFactory();
        using var hostConfiguration = new HostConfigurationScope();
        using var application = factory.WithWebHostBuilder(_ => { });
        var videoId = await SeedVideoAsync(application, "v-reference", "Saved clip", isReference: true);
        using var client = application.CreateClient();

        var response = await client.PostAsJsonAsync("/api/videos/unmark-reference", new { videoId });

        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);

        var stored = Assert.Single(await ReadVideosAsync(application, videoId));
        Assert.False(stored.IsReference);
        Assert.Equal(VideoTrackingStatus.ACTIVE, stored.TrackingStatus);
        Assert.Null(stored.ReferenceSetAt);
    }

    [Fact]
    public async Task Unmark_reference_returns_a_readable_404_for_a_non_reference_or_missing_video()
    {
        using var factory = new ChannelApiFactory();
        using var hostConfiguration = new HostConfigurationScope();
        using var application = factory.WithWebHostBuilder(_ => { });
        var activeId = await SeedVideoAsync(application, "v-active", "Active clip");
        using var client = application.CreateClient();

        var nonReference = await client.PostAsJsonAsync("/api/videos/unmark-reference", new { videoId = activeId });
        var missing = await client.PostAsJsonAsync("/api/videos/unmark-reference", new { videoId = Guid.NewGuid() });

        Assert.Equal(HttpStatusCode.NotFound, nonReference.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, missing.StatusCode);

        using var document = JsonDocument.Parse(await nonReference.Content.ReadAsStringAsync());
        Assert.Equal(
            "This video is no longer in the reference list. Refresh the list and try again.",
            document.RootElement.GetProperty("detail").GetString());

        // The active row is untouched by a refused removal.
        var stored = Assert.Single(await ReadVideosAsync(application, activeId));
        Assert.False(stored.IsReference);
        Assert.Equal(VideoTrackingStatus.ACTIVE, stored.TrackingStatus);
    }

    [Fact]
    public async Task Unmark_reference_rejects_a_missing_or_empty_video_id()
    {
        using var factory = new ChannelApiFactory();
        using var hostConfiguration = new HostConfigurationScope();
        using var application = factory.WithWebHostBuilder(_ => { });
        using var client = application.CreateClient();

        var empty = await client.PostAsJsonAsync("/api/videos/unmark-reference", new { videoId = Guid.Empty });
        var absent = await client.PostAsJsonAsync("/api/videos/unmark-reference", new { });

        Assert.Equal(HttpStatusCode.BadRequest, empty.StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, absent.StatusCode);

        using var document = JsonDocument.Parse(await empty.Content.ReadAsStringAsync());
        Assert.True(document.RootElement.GetProperty("errors").TryGetProperty("videoId", out _));
    }

    [Theory]
    [InlineData("/api/videos/active/list")]
    [InlineData("/api/videos/reference/list")]
    public async Task List_actions_reject_an_unknown_sort_field_or_direction(string route)
    {
        using var factory = new ChannelApiFactory();
        using var hostConfiguration = new HostConfigurationScope();
        using var application = factory.WithWebHostBuilder(_ => { });
        using var client = application.CreateClient();

        var unknownField = await client.PostAsJsonAsync(
            route,
            new { page = 1, pageSize = 10, searchText = (string?)null, sortBy = "channelName", sortDirection = "desc" });
        var unknownDirection = await client.PostAsJsonAsync(
            route,
            new { page = 1, pageSize = 10, searchText = (string?)null, sortBy = "views", sortDirection = "sideways" });

        Assert.Equal(HttpStatusCode.BadRequest, unknownField.StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, unknownDirection.StatusCode);

        using var fieldDocument = JsonDocument.Parse(await unknownField.Content.ReadAsStringAsync());
        using var directionDocument = JsonDocument.Parse(await unknownDirection.Content.ReadAsStringAsync());
        Assert.True(fieldDocument.RootElement.GetProperty("errors").TryGetProperty("sortBy", out _));
        Assert.True(directionDocument.RootElement.GetProperty("errors").TryGetProperty("sortDirection", out _));
    }

    [Theory]
    [InlineData("/api/videos/active/list")]
    [InlineData("/api/videos/reference/list")]
    public async Task List_actions_accept_every_permitted_sort_field_and_direction(string route)
    {
        using var factory = new ChannelApiFactory();
        using var hostConfiguration = new HostConfigurationScope();
        using var application = factory.WithWebHostBuilder(_ => { });
        await SeedVideoAsync(application, "v-1", "One", isReference: true);
        using var client = application.CreateClient();

        foreach (var field in new[] { "title", "views", "likes", "publishedAt", "lastUpdatedAt" })
        {
            foreach (var direction in new[] { "asc", "desc" })
            {
                var response = await client.PostAsJsonAsync(
                    route,
                    new { page = 1, pageSize = 10, searchText = (string?)null, sortBy = field, sortDirection = direction });

                Assert.True(
                    response.StatusCode == HttpStatusCode.OK,
                    $"{route} rejected {field}/{direction} with {(int)response.StatusCode}.");
            }
        }
    }

    private static async Task AssertVideoIdsProblemAsync(HttpClient client, object body)
    {
        var response = await client.PostAsJsonAsync("/api/videos/mark-reference", body);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.True(document.RootElement.GetProperty("errors").TryGetProperty("videoIds", out _));
    }

    private static async Task<Guid> SeedVideoAsync(
        WebApplicationFactory<Program> application,
        string youtubeVideoId,
        string title,
        bool isReference = false,
        VideoTrackingStatus trackingStatus = VideoTrackingStatus.ACTIVE)
    {
        using var scope = application.Services.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<YtFetcherDbContext>();

        var channel = new Channel
        {
            Id = Guid.NewGuid(),
            // Deterministic so a test can assert the provider channel id the list returns.
            YoutubeChannelId = $"UC-{youtubeVideoId}",
            Name = "Reference Channel",
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
            CurrentViews = 4_200,
            CurrentLikes = 310,
            IsReference = isReference,
            TrackingStatus = isReference ? VideoTrackingStatus.STOPPED : trackingStatus,
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

    private static async Task<List<Video>> ReadVideosAsync(WebApplicationFactory<Program> application, params Guid[] ids)
    {
        using var scope = application.Services.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<YtFetcherDbContext>();

        return await context.Videos
            .AsNoTracking()
            .Where(video => ids.Contains(video.Id))
            .ToListAsync();
    }
}
