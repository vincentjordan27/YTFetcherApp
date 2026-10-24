using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using YtFetcher.Application.Abstractions.Services;
using YtFetcher.Application.Channels.DTO.Result;
using YtFetcher.Core.Channels;
using YtFetcher.Infrastructure.Persistence;

namespace YtFetcher.Api.Tests.Channels;

/// <summary>
/// HTTP contract for the manual add action: body shape, one lookup, the exact status behavior, and
/// the lifecycle-specific duplicate behavior.
/// </summary>
[Collection(ApiStartupCollection.Name)]
public class ManualApproveChannelEndpointTests
{
    private const string UnsupportedReferenceMessage = "Enter a YouTube handle starting with @.";
    private const string UploadsPlaylistUnavailableMessage =
        "This YouTube channel cannot be added because its uploads playlist is unavailable.";
    private const string ChannelNotFoundMessage = "The YouTube channel could not be found.";
    private const string ProviderUnavailableMessage =
        "The YouTube channel could not be checked right now. Try again later.";

    private static readonly DateTimeOffset Now = new(2026, 10, 1, 18, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task Manual_approve_creates_an_approved_channel_that_is_scheduled()
    {
        using var databaseConfiguration = new HostConfigurationScope();
        using var factory = new ChannelApiFactory();
        using var application = CreateHost(
            factory,
            Found("UC-new", "New Channel", "https://example.com/new.jpg", "UU-new"));
        using var client = application.CreateClient();

        var response = await client.PostAsJsonAsync(
            "/api/channels/manual-approve",
            new { channelReference = "@newchannel" });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var payload = await response.Content.ReadFromJsonAsync<ManualAddPayload>();
        Assert.NotNull(payload);
        Assert.Equal("APPROVED", payload.Status);
        // SCHEDULING proves the existing scheduling path ran without writing the forbidden PENDING transition.
        Assert.Equal("SCHEDULING", payload.BackfillStatus);

        var stored = await ReadByYoutubeChannelIdAsync(application, "UC-new");
        Assert.NotNull(stored);
        Assert.Equal("New Channel", stored.Name);
        Assert.Equal("UU-new", stored.UploadsPlaylistId);
        Assert.Equal(ChannelStatus.APPROVED, stored.Status);
        Assert.Equal(BackfillStatus.SCHEDULING, stored.BackfillStatus);
    }

    [Fact]
    public async Task Manual_approve_uses_the_same_approval_path_when_the_channel_is_already_pending()
    {
        using var databaseConfiguration = new HostConfigurationScope();
        using var factory = new ChannelApiFactory();
        using var application = CreateHost(
            factory,
            Found("UC-existing-pending", "Existing Pending Channel", null, "UU-existing-pending"));
        await SeedAsync(application, ExistingChannel("UC-existing-pending", ChannelStatus.PENDING, isDeleted: false));
        using var client = application.CreateClient();

        var response = await client.PostAsJsonAsync(
            "/api/channels/manual-approve",
            new { channelReference = "@existingpending" });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var payload = await response.Content.ReadFromJsonAsync<ManualAddPayload>();
        Assert.NotNull(payload);
        Assert.Equal("APPROVED", payload.Status);
        Assert.Equal("SCHEDULING", payload.BackfillStatus);

        var stored = await ReadByYoutubeChannelIdAsync(application, "UC-existing-pending");
        Assert.NotNull(stored);
        Assert.Equal(ChannelStatus.APPROVED, stored.Status);
        Assert.Equal(BackfillStatus.SCHEDULING, stored.BackfillStatus);
    }

    [Theory]
    [InlineData("https://www.youtube.com/@example")]
    [InlineData("UC1234567890")]
    [InlineData("example")]
    [InlineData("@")]
    public async Task Manual_approve_rejects_an_unsupported_reference_with_the_readable_message(string reference)
    {
        using var databaseConfiguration = new HostConfigurationScope();
        using var factory = new ChannelApiFactory();
        using var application = CreateHost(factory, ManualChannelLookupResult.NotFound());
        using var client = application.CreateClient();

        var response = await client.PostAsJsonAsync(
            "/api/channels/manual-approve",
            new { channelReference = reference });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.Equal(
            UnsupportedReferenceMessage,
            document.RootElement.GetProperty("errors").GetProperty("channelReference")[0].GetString());
    }

    [Fact]
    public async Task Manual_approve_rejects_a_missing_body_field_at_the_transport_boundary()
    {
        using var databaseConfiguration = new HostConfigurationScope();
        using var factory = new ChannelApiFactory();
        using var application = CreateHost(factory, ManualChannelLookupResult.NotFound());
        using var client = application.CreateClient();

        var response = await client.PostAsJsonAsync("/api/channels/manual-approve", new { });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.True(document.RootElement.GetProperty("errors").TryGetProperty("channelReference", out _));
    }

    [Fact]
    public async Task Manual_approve_returns_not_found_when_youtube_has_no_such_handle()
    {
        using var databaseConfiguration = new HostConfigurationScope();
        using var factory = new ChannelApiFactory();
        using var application = CreateHost(factory, ManualChannelLookupResult.NotFound());
        using var client = application.CreateClient();

        var response = await client.PostAsJsonAsync(
            "/api/channels/manual-approve",
            new { channelReference = "@missing" });

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.Equal(ChannelNotFoundMessage, await ReadDetailAsync(response));
    }

    [Fact]
    public async Task Manual_approve_returns_service_unavailable_when_youtube_cannot_be_reached()
    {
        using var databaseConfiguration = new HostConfigurationScope();
        using var factory = new ChannelApiFactory();
        using var application = CreateHost(factory, ManualChannelLookupResult.Unavailable());
        using var client = application.CreateClient();

        var response = await client.PostAsJsonAsync(
            "/api/channels/manual-approve",
            new { channelReference = "@flaky" });

        Assert.Equal(HttpStatusCode.ServiceUnavailable, response.StatusCode);
        Assert.Equal(ProviderUnavailableMessage, await ReadDetailAsync(response));
    }

    [Fact]
    public async Task Manual_approve_rejects_a_channel_without_an_uploads_playlist()
    {
        using var databaseConfiguration = new HostConfigurationScope();
        using var factory = new ChannelApiFactory();
        using var application = CreateHost(
            factory,
            ManualChannelLookupResult.Found("UC-no-uploads", "No Uploads", null, uploadsPlaylistId: null));
        using var client = application.CreateClient();

        var response = await client.PostAsJsonAsync(
            "/api/channels/manual-approve",
            new { channelReference = "@nouploads" });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.Equal(
            UploadsPlaylistUnavailableMessage,
            document.RootElement.GetProperty("errors").GetProperty("channelReference")[0].GetString());
    }

    [Theory]
    [InlineData(ChannelStatus.APPROVED, false, "APPROVED", "This channel is already approved.")]
    [InlineData(ChannelStatus.REJECTED, false, "REJECTED", "This channel was rejected and cannot be added again.")]
    [InlineData(ChannelStatus.APPROVED, true, "DELETED", "This channel was deleted and cannot be added again.")]
    public async Task Manual_approve_returns_the_fixed_duplicate_conflict(
        ChannelStatus status,
        bool isDeleted,
        string expectedStatus,
        string expectedMessage)
    {
        using var databaseConfiguration = new HostConfigurationScope();
        using var factory = new ChannelApiFactory();
        using var application = CreateHost(
            factory,
            Found("UC-existing", "Existing Channel", null, "UU-existing"));
        await SeedAsync(application, ExistingChannel("UC-existing", status, isDeleted));
        using var client = application.CreateClient();

        var response = await client.PostAsJsonAsync(
            "/api/channels/manual-approve",
            new { channelReference = "@existing" });

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.Equal(expectedMessage, document.RootElement.GetProperty("detail").GetString());
        Assert.Equal(expectedStatus, document.RootElement.GetProperty("currentStatus").GetString());

        // The durable row was never changed or revived.
        var stored = await ReadByYoutubeChannelIdAsync(application, "UC-existing");
        Assert.NotNull(stored);
        Assert.Equal(isDeleted, stored.IsDeleted);
    }

    [Fact]
    public async Task Manual_approve_rejects_an_overlong_reference()
    {
        using var databaseConfiguration = new HostConfigurationScope();
        using var factory = new ChannelApiFactory();
        using var application = CreateHost(factory, ManualChannelLookupResult.NotFound());
        using var client = application.CreateClient();

        var response = await client.PostAsJsonAsync(
            "/api/channels/manual-approve",
            new { channelReference = "@" + new string('a', 500) });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.True(document.RootElement.GetProperty("errors").TryGetProperty("channelReference", out _));
    }

    private static WebApplicationFactory<Program> CreateHost(
        ChannelApiFactory factory,
        ManualChannelLookupResult lookupResult) =>
        factory.WithWebHostBuilder(builder => builder.ConfigureServices(services =>
        {
            services.RemoveAll<IYouTubeChannelLookupGateway>();
            services.AddSingleton<IYouTubeChannelLookupGateway>(new FakeLookupGateway(lookupResult));
        }));

    private static ManualChannelLookupResult Found(
        string youtubeChannelId,
        string name,
        string? thumbnailUrl,
        string? uploadsPlaylistId) =>
        ManualChannelLookupResult.Found(youtubeChannelId, name, thumbnailUrl, uploadsPlaylistId);

    private static Channel ExistingChannel(string youtubeChannelId, ChannelStatus status, bool isDeleted) => new()
    {
        Id = Guid.NewGuid(),
        YoutubeChannelId = youtubeChannelId,
        Name = "Existing Channel",
        Status = status,
        BackfillStatus = BackfillStatus.PENDING,
        IsDeleted = isDeleted,
        DiscoveredAt = Now.AddDays(-1),
        CreatedAt = Now.AddDays(-1),
        ModifiedAt = Now.AddDays(-1)
    };

    private static async Task SeedAsync(WebApplicationFactory<Program> application, Channel channel)
    {
        using var scope = application.Services.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<YtFetcherDbContext>();
        dbContext.Channels.Add(channel);
        await dbContext.SaveChangesAsync();
    }

    private static async Task<Channel?> ReadByYoutubeChannelIdAsync(
        WebApplicationFactory<Program> application,
        string youtubeChannelId)
    {
        using var scope = application.Services.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<YtFetcherDbContext>();
        return await dbContext.Channels
            .AsNoTracking()
            .SingleOrDefaultAsync(channel => channel.YoutubeChannelId == youtubeChannelId);
    }

    private static async Task<string?> ReadDetailAsync(HttpResponseMessage response)
    {
        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        return document.RootElement.GetProperty("detail").GetString();
    }

    private sealed record ManualAddPayload(string Status, string BackfillStatus);

    private sealed class FakeLookupGateway(ManualChannelLookupResult result) : IYouTubeChannelLookupGateway
    {
        public Task<ManualChannelLookupResult> LookupByHandleAsync(
            string handle,
            CancellationToken cancellationToken) =>
            Task.FromResult(result);
    }
}
