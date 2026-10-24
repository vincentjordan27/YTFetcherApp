using System.Net;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using YtFetcher.Core.Channels;
using YtFetcher.Infrastructure.Persistence;

namespace YtFetcher.Api.Tests.Channels;

/// <summary>
/// HTTP contract of the operator retry for a terminally failed backfill page. The endpoint must resume
/// the durable cursor without moving progress, and it must refuse anything that is not a failed page.
/// </summary>
[Collection(ApiStartupCollection.Name)]
public class ChannelBackfillRetryEndpointTests
{
    private const string ContinuationCursor = "cursor-page-2";

    private static readonly DateTimeOffset Now = new(2026, 10, 5, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task Retry_returns_the_channel_to_pending_and_keeps_the_durable_page_state()
    {
        using var databaseConfiguration = new HostConfigurationScope();
        using var application = new ChannelApiFactory();
        var channel = FailedChannel();

        await SeedAsync(application, channel);
        using var client = application.CreateClient();

        var before = DateTimeOffset.UtcNow;
        var response = await client.PostAsJsonAsync($"/api/channels/{channel.Id}/backfill/retry", new { });
        var after = DateTimeOffset.UtcNow;

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var stored = await ReadChannelAsync(application, channel.Id);
        Assert.Equal(BackfillStatus.PENDING, stored.BackfillStatus);
        Assert.Equal(0, stored.BackfillFailureCount);
        Assert.Null(stored.BackfillFailureReason);
        Assert.Null(stored.BackfillLastFailedAt);

        // The host resolves TimeProvider.System, so the ready time is asserted against the window around
        // the call rather than one fixed instant.
        Assert.NotNull(stored.BackfillReadyAt);
        Assert.InRange(stored.BackfillReadyAt.Value, before.AddSeconds(-1), after.AddSeconds(1));

        // The retry must resume exactly the page that failed.
        Assert.Equal(ContinuationCursor, stored.BackfillPageToken);
        Assert.Equal(1, stored.BackfillPagesCompleted);
        Assert.Equal(Now.AddDays(-Channel.BackfillHorizonDays), stored.BackfillCutoffAt);
    }

    [Theory]
    [InlineData(BackfillStatus.PENDING)]
    [InlineData(BackfillStatus.SCHEDULING)]
    [InlineData(BackfillStatus.SCHEDULING_FAILED)]
    [InlineData(BackfillStatus.RUNNING)]
    [InlineData(BackfillStatus.COMPLETED)]
    public async Task Retry_is_a_conflict_when_the_backfill_is_not_terminally_failed(BackfillStatus backfillStatus)
    {
        using var databaseConfiguration = new HostConfigurationScope();
        using var application = new ChannelApiFactory();
        var channel = FailedChannel();
        channel.BackfillStatus = backfillStatus;

        await SeedAsync(application, channel);
        using var client = application.CreateClient();

        var response = await client.PostAsJsonAsync($"/api/channels/{channel.Id}/backfill/retry", new { });

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);

        var stored = await ReadChannelAsync(application, channel.Id);
        Assert.Equal(backfillStatus, stored.BackfillStatus);
        Assert.Equal(Channel.BackfillFailureLimit, stored.BackfillFailureCount);
        Assert.Equal("Unable to retrieve uploads from YouTube. Try again later.", stored.BackfillFailureReason);
    }

    [Fact]
    public async Task Retry_is_a_conflict_for_a_rejected_channel()
    {
        using var databaseConfiguration = new HostConfigurationScope();
        using var application = new ChannelApiFactory();
        var channel = FailedChannel();
        channel.Status = ChannelStatus.REJECTED;

        await SeedAsync(application, channel);
        using var client = application.CreateClient();

        var response = await client.PostAsJsonAsync($"/api/channels/{channel.Id}/backfill/retry", new { });

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        Assert.Equal(BackfillStatus.FAILED, (await ReadChannelAsync(application, channel.Id)).BackfillStatus);
    }

    [Fact]
    public async Task Retry_is_not_found_for_an_unknown_channel()
    {
        using var databaseConfiguration = new HostConfigurationScope();
        using var application = new ChannelApiFactory();
        using var client = application.CreateClient();

        var response = await client.PostAsJsonAsync($"/api/channels/{Guid.NewGuid()}/backfill/retry", new { });

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task Retry_rejects_an_invalid_channel_id_with_the_existing_validation_problem()
    {
        using var databaseConfiguration = new HostConfigurationScope();
        using var application = new ChannelApiFactory();
        using var client = application.CreateClient();

        var response = await client.PostAsJsonAsync("/api/channels/not-a-guid/backfill/retry", new { });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);

        var problem = await response.Content.ReadFromJsonAsync<ValidationProblemPayload>();
        Assert.NotNull(problem);
        Assert.True(problem.Errors.ContainsKey("channelId"));
    }

    private static Channel FailedChannel()
    {
        var channel = new Channel
        {
            Id = Guid.NewGuid(),
            YoutubeChannelId = $"UC-{Guid.NewGuid():N}",
            Name = "Backfill retry channel",
            Status = ChannelStatus.APPROVED,
            ApprovedAt = Now.AddDays(-2),
            DiscoveredAt = Now.AddDays(-3),
            CreatedAt = Now.AddDays(-3),
            ModifiedAt = Now.AddDays(-1),
            IsDeleted = false,
            BackfillStatus = BackfillStatus.FAILED,
            BackfillPageToken = ContinuationCursor,
            BackfillPagesCompleted = 1,
            BackfillCutoffAt = Now.AddDays(-Channel.BackfillHorizonDays),
            BackfillFailureCount = Channel.BackfillFailureLimit,
            BackfillFailureReason = "Unable to retrieve uploads from YouTube. Try again later.",
            BackfillLastFailedAt = Now.AddMinutes(-30),
            BackfillReadyAt = Now.AddDays(-1)
        };

        return channel;
    }

    private static async Task SeedAsync(WebApplicationFactory<Program> factory, params Channel[] channels)
    {
        using var scope = factory.Services.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<YtFetcherDbContext>();
        dbContext.Channels.AddRange(channels);
        await dbContext.SaveChangesAsync();
    }

    private static async Task<Channel> ReadChannelAsync(WebApplicationFactory<Program> factory, Guid channelId)
    {
        using var scope = factory.Services.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<YtFetcherDbContext>();
        return await dbContext.Channels.AsNoTracking().SingleAsync(channel => channel.Id == channelId);
    }

    private sealed record ValidationProblemPayload(IDictionary<string, string[]> Errors);
}
