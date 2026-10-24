using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using YtFetcher.Core.BackfillRuns;
using YtFetcher.Application.Abstractions.Services;
using YtFetcher.Core.Channels;
using YtFetcher.Infrastructure.Persistence;

namespace YtFetcher.Api.Tests.Channels;

/// <summary>
/// HTTP contract for the scheduling states: approval never fails on a scheduler error, the explicit
/// retry endpoint is state-guarded, and scheduling fields are exclusive to the scheduling statuses.
/// </summary>
[Collection(ApiStartupCollection.Name)]
public class ChannelSchedulingEndpointTests
{
    private static readonly DateTimeOffset Now = new(2026, 9, 30, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task Approve_returns_scheduling_after_a_successful_enqueue()
    {
        using var databaseConfiguration = new HostConfigurationScope();
        using var application = new ChannelApiFactory();
        var channel = PendingChannel();

        await SeedAsync(application, channel);
        using var client = application.CreateClient();

        var response = await client.PostAsJsonAsync($"/api/channels/{channel.Id}/approve", new { });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var detail = await response.Content.ReadFromJsonAsync<SchedulingDetailPayload>();
        Assert.NotNull(detail);
        Assert.Equal("APPROVED", detail.Status);
        Assert.Equal("SCHEDULING", detail.BackfillStatus);
        Assert.Null(detail.BackfillSchedulingFailureReason);

        var stored = await ReadChannelAsync(application, channel.Id);
        Assert.Equal(BackfillStatus.SCHEDULING, stored.BackfillStatus);
        Assert.NotNull(stored.BackfillSchedulingAttemptedAt);
    }

    [Fact]
    public async Task Approve_returns_scheduling_failed_when_the_scheduler_throws()
    {
        using var databaseConfiguration = new HostConfigurationScope();
        using var factory = new ChannelApiFactory();
        using var application = factory.WithWebHostBuilder(builder =>
            builder.ConfigureServices(services =>
            {
                services.RemoveAll<IBackfillScheduler>();
                services.AddSingleton<IBackfillScheduler>(new ThrowingBackfillScheduler());
            }));
        var channel = PendingChannel();

        await SeedAsync(application, channel);
        using var client = application.CreateClient();

        var response = await client.PostAsJsonAsync($"/api/channels/{channel.Id}/approve", new { });

        // The approval committed, so a scheduler error is never a server error.
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var detail = await response.Content.ReadFromJsonAsync<SchedulingDetailPayload>();
        Assert.NotNull(detail);
        Assert.Equal("APPROVED", detail.Status);
        Assert.Equal("SCHEDULING_FAILED", detail.BackfillStatus);
        Assert.Equal("Backfill could not be scheduled. Try again.", detail.BackfillSchedulingFailureReason);

        var stored = await ReadChannelAsync(application, channel.Id);
        Assert.Equal(BackfillStatus.SCHEDULING_FAILED, stored.BackfillStatus);
        Assert.Equal(0, stored.BackfillFailureCount);
        Assert.Null(stored.BackfillFailureReason);
    }

    [Fact]
    public async Task Approved_ledger_exposes_scheduling_fields_only_for_scheduling_states()
    {
        using var databaseConfiguration = new HostConfigurationScope();
        using var application = new ChannelApiFactory();

        await SeedAsync(
            application,
            SchedulingChannel(BackfillStatus.SCHEDULING, reason: null),
            SchedulingChannel(BackfillStatus.SCHEDULING_FAILED, reason: "Backfill could not be scheduled. Try again."),
            SchedulingChannel(BackfillStatus.FAILED, reason: null));

        using var client = application.CreateClient();
        var response = await client.PostAsJsonAsync(
            "/api/channels/approved/list",
            new { page = 1, pageSize = 10, searchText = (string?)null });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var items = document.RootElement.GetProperty("items").EnumerateArray()
            .ToDictionary(item => item.GetProperty("backfillStatus").GetString()!);

        var scheduling = items["SCHEDULING"];
        Assert.Equal(JsonValueKind.String, scheduling.GetProperty("backfillSchedulingAttemptedAt").ValueKind);
        Assert.Equal(JsonValueKind.Null, scheduling.GetProperty("backfillSchedulingFailureReason").ValueKind);

        var schedulingFailed = items["SCHEDULING_FAILED"];
        Assert.Equal(JsonValueKind.String, schedulingFailed.GetProperty("backfillSchedulingAttemptedAt").ValueKind);
        Assert.Equal(
            "Backfill could not be scheduled. Try again.",
            schedulingFailed.GetProperty("backfillSchedulingFailureReason").GetString());

        var failed = items["FAILED"];
        Assert.Equal(JsonValueKind.Null, failed.GetProperty("backfillSchedulingAttemptedAt").ValueKind);
        Assert.Equal(JsonValueKind.Null, failed.GetProperty("backfillSchedulingFailureReason").ValueKind);
    }

    private static Channel PendingChannel() => new()
    {
        Id = Guid.NewGuid(),
        YoutubeChannelId = $"UC-{Guid.NewGuid():N}",
        Name = "Scheduling endpoint channel",
        Status = ChannelStatus.PENDING,
        BackfillStatus = BackfillStatus.PENDING,
        IsDeleted = false,
        DiscoveredAt = Now.AddDays(-2),
        CreatedAt = Now.AddDays(-2),
        ModifiedAt = Now.AddDays(-1)
    };

    private static Channel SchedulingChannel(BackfillStatus backfillStatus, string? reason)
    {
        var channel = PendingChannel();
        channel.Status = ChannelStatus.APPROVED;
        channel.ApprovedAt = Now;
        channel.BackfillStatus = backfillStatus;
        channel.BackfillSchedulingAttemptedAt = Now.AddMinutes(-1);
        channel.BackfillSchedulingFailureReason = reason;
        channel.ModifiedAt = Now;
        return channel;
    }

    private static Channel SchedulingFailedChannel()
    {
        var channel = PendingChannel();
        channel.Status = ChannelStatus.APPROVED;
        channel.ApprovedAt = Now;
        channel.BackfillStatus = BackfillStatus.SCHEDULING_FAILED;
        channel.BackfillSchedulingAttemptedAt = Now.AddMinutes(-1);
        channel.BackfillSchedulingFailureReason = "Backfill could not be scheduled. Try again.";
        channel.ModifiedAt = Now;
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

    private sealed class ThrowingBackfillScheduler : IBackfillScheduler
    {
        public void EnqueuePage(Guid channelId, BackfillTriggerSource triggerSource) =>
            throw new InvalidOperationException("scheduler unavailable");
    }

    private sealed record SchedulingDetailPayload(
        string Status,
        string BackfillStatus,
        DateTimeOffset? BackfillSchedulingAttemptedAt,
        string? BackfillSchedulingFailureReason);
}
