using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using YtFetcher.Api.Tests.Channels;
using YtFetcher.Core.Channels;
using YtFetcher.Infrastructure.Persistence;

namespace YtFetcher.Api.Tests.Channels;

/// <summary>
/// The approved ledger item contract carries the persisted backfill failure fields, so the failed
/// badge and its detail dialog can render only API-provided values.
/// </summary>
[Collection(ApiStartupCollection.Name)]
public class ApprovedChannelFailureContractTests
{
    private static readonly DateTimeOffset Now = new(2026, 9, 30, 12, 0, 0, TimeSpan.Zero);

    private const string SafeReason = "Unable to retrieve uploads from YouTube. Try again later.";

    [Fact]
    public async Task A_failed_channel_exposes_its_count_safe_reason_and_timestamp()
    {
        using var factory = new ChannelApiFactory();
        using var hostConfiguration = new HostConfigurationScope();
        using var application = factory.WithWebHostBuilder(_ => { });

        await SeedAsync(application, BackfillStatus.FAILED, failureCount: 2, SafeReason);
        using var client = application.CreateClient();

        var response = await client.PostAsJsonAsync(
            "/api/channels/approved/list",
            new { page = 1, pageSize = 10, searchText = (string?)null });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var item = Assert.Single(document.RootElement.GetProperty("items").EnumerateArray().ToList());
        Assert.Equal("FAILED", item.GetProperty("backfillStatus").GetString());
        Assert.Equal(2, item.GetProperty("backfillFailureCount").GetInt32());
        Assert.Equal(SafeReason, item.GetProperty("backfillFailureReason").GetString());
        Assert.Equal(Now.AddMinutes(-5), item.GetProperty("backfillLastFailedAt").GetDateTimeOffset());
    }

    [Theory]
    [InlineData(BackfillStatus.PENDING)]
    [InlineData(BackfillStatus.RUNNING)]
    [InlineData(BackfillStatus.COMPLETED)]
    public async Task A_channel_that_did_not_fail_reports_zero_and_two_nulls(BackfillStatus backfillStatus)
    {
        using var factory = new ChannelApiFactory();
        using var hostConfiguration = new HostConfigurationScope();
        using var application = factory.WithWebHostBuilder(_ => { });

        await SeedAsync(application, backfillStatus, failureCount: 0, reason: null);
        using var client = application.CreateClient();

        var response = await client.PostAsJsonAsync(
            "/api/channels/approved/list",
            new { page = 1, pageSize = 10, searchText = (string?)null });

        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var item = Assert.Single(document.RootElement.GetProperty("items").EnumerateArray().ToList());
        Assert.Equal(0, item.GetProperty("backfillFailureCount").GetInt32());
        Assert.Equal(JsonValueKind.Null, item.GetProperty("backfillFailureReason").ValueKind);
        Assert.Equal(JsonValueKind.Null, item.GetProperty("backfillLastFailedAt").ValueKind);
    }

    [Fact]
    public async Task An_internally_failed_pending_channel_hides_its_retry_data()
    {
        using var factory = new ChannelApiFactory();
        using var hostConfiguration = new HostConfigurationScope();
        using var application = factory.WithWebHostBuilder(_ => { });

        // A channel whose first or second execution failure is still retryable keeps the count,
        // reason and timestamp internally but must look like an ordinary pending channel over HTTP.
        await SeedAsync(application, BackfillStatus.PENDING, failureCount: 2, SafeReason);
        using var client = application.CreateClient();

        var response = await client.PostAsJsonAsync(
            "/api/channels/approved/list",
            new { page = 1, pageSize = 10, searchText = (string?)null });

        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var item = Assert.Single(document.RootElement.GetProperty("items").EnumerateArray().ToList());
        Assert.Equal("PENDING", item.GetProperty("backfillStatus").GetString());
        Assert.Equal(0, item.GetProperty("backfillFailureCount").GetInt32());
        Assert.Equal(JsonValueKind.Null, item.GetProperty("backfillFailureReason").ValueKind);
        Assert.Equal(JsonValueKind.Null, item.GetProperty("backfillLastFailedAt").ValueKind);
    }

    private static async Task SeedAsync(
        WebApplicationFactory<Program> application,
        BackfillStatus backfillStatus,
        int failureCount,
        string? reason)
    {
        using var scope = application.Services.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<YtFetcherDbContext>();

        context.Channels.Add(new Channel
        {
            Id = Guid.NewGuid(),
            YoutubeChannelId = $"UC-{Guid.NewGuid():N}",
            Name = "Failure contract channel",
            Status = ChannelStatus.APPROVED,
            ApprovedAt = Now,
            BackfillStatus = backfillStatus,
            BackfillFailureCount = failureCount,
            BackfillFailureReason = reason,
            BackfillLastFailedAt = reason is null ? null : Now.AddMinutes(-5),
            IsDeleted = false,
            DiscoveredAt = Now,
            CreatedAt = Now,
            ModifiedAt = Now
        });

        await context.SaveChangesAsync();
    }
}
