using System.Net;
using System.Net.Http.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using YtFetcher.Core.Channels;
using YtFetcher.Infrastructure.Persistence;

namespace YtFetcher.Api.Tests.Channels;

/// <summary>
/// Approved and rejected ledgers plus the navigation count summary: ordering, search, paging,
/// soft-delete exclusion, and real counts.
/// </summary>
[Collection(ApiStartupCollection.Name)]
public class ChannelLedgerEndpointTests
{
    private static readonly DateTimeOffset Base = new(2026, 9, 20, 0, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task Approved_list_orders_by_approval_newest_first_and_excludes_soft_deleted_channels()
    {
        using var databaseConfiguration = new HostConfigurationScope();
        using var application = new ChannelApiFactory();

        var older = ApprovedChannel("UC-approved-old", Base.AddDays(1));
        var newer = ApprovedChannel("UC-approved-new", Base.AddDays(3));
        var deleted = ApprovedChannel("UC-approved-deleted", Base.AddDays(4));
        deleted.IsDeleted = true;
        deleted.DeletedAt = Base.AddDays(4);
        var pending = PendingChannel("UC-still-pending", Base.AddDays(5));

        await SeedAsync(application, older, newer, deleted, pending);

        var client = application.CreateClient();
        var payload = await ListAsync<ApprovedListPayload>(client, "approved", 1, 10);

        Assert.NotNull(payload);
        Assert.Equal(2, payload.TotalCount);
        Assert.Equal([newer.Id, older.Id], payload.Items.Select(item => item.Id));
        Assert.Equal("RUNNING", payload.Items[0].BackfillStatus);
        Assert.Null(payload.Items[0].LastUploadCheckAt);
    }

    [Fact]
    public async Task Approved_list_searches_name_and_channel_id_and_clamps_to_the_requested_page()
    {
        using var databaseConfiguration = new HostConfigurationScope();
        using var application = new ChannelApiFactory();

        await SeedAsync(
            application,
            ApprovedChannel("UC-kyoto-craft", Base.AddDays(3)),
            ApprovedChannel("UC-kyoto-clay", Base.AddDays(2)),
            ApprovedChannel("UC-osaka-metal", Base.AddDays(1)));

        var client = application.CreateClient();

        var byName = await ListAsync<ApprovedListPayload>(client, "approved", 1, 10, "KYOTO");
        Assert.NotNull(byName);
        Assert.Equal(2, byName.TotalCount);

        var byChannelId = await ListAsync<ApprovedListPayload>(client, "approved", 1, 10, "uc-OSAKA");
        Assert.NotNull(byChannelId);
        Assert.Equal(1, byChannelId.TotalCount);

        var firstPage = await ListAsync<ApprovedListPayload>(client, "approved", 1, 2);
        Assert.NotNull(firstPage);
        Assert.Equal(2, firstPage.Items.Count);
        Assert.Equal(3, firstPage.TotalCount);

        var secondPage = await ListAsync<ApprovedListPayload>(client, "approved", 2, 2);
        Assert.NotNull(secondPage);
        Assert.Single(secondPage.Items);
    }

    [Fact]
    public async Task Approved_list_rejects_invalid_paging_and_a_missing_body()
    {
        using var databaseConfiguration = new HostConfigurationScope();
        using var application = new ChannelApiFactory();
        var client = application.CreateClient();

        Assert.Equal(
            HttpStatusCode.BadRequest,
            (await client.PostAsJsonAsync("/api/channels/approved/list", new { page = 0, pageSize = 10 })).StatusCode);

        Assert.Equal(
            HttpStatusCode.BadRequest,
            (await client.PostAsJsonAsync("/api/channels/approved/list", new { page = 1, pageSize = 101 })).StatusCode);

        Assert.Equal(
            HttpStatusCode.BadRequest,
            (await client.PostAsync("/api/channels/approved/list", null)).StatusCode);
    }

    [Fact]
    public async Task Rejected_list_returns_the_persisted_rejection_reason_newest_first()
    {
        using var databaseConfiguration = new HostConfigurationScope();
        using var application = new ChannelApiFactory();

        var first = RejectedChannel("UC-rejected-1", Base.AddDays(1), "Duplicate entity");
        var second = RejectedChannel("UC-rejected-2", Base.AddDays(4), "Irrelevant content for this niche");
        await SeedAsync(application, first, second);

        var client = application.CreateClient();
        var payload = await ListAsync<RejectedListPayload>(client, "rejected", 1, 10);

        Assert.NotNull(payload);
        Assert.Equal(2, payload.TotalCount);
        Assert.Equal([second.Id, first.Id], payload.Items.Select(item => item.Id));
        Assert.Equal("Irrelevant content for this niche", payload.Items[0].RejectionReason);
        Assert.Equal("Duplicate entity", payload.Items[1].RejectionReason);
    }

    [Fact]
    public async Task Review_counts_return_the_three_real_non_deleted_totals()
    {
        using var databaseConfiguration = new HostConfigurationScope();
        using var application = new ChannelApiFactory();

        var deletedPending = PendingChannel("UC-count-deleted", Base.AddDays(9));
        deletedPending.IsDeleted = true;
        deletedPending.DeletedAt = Base.AddDays(9);

        await SeedAsync(
            application,
            PendingChannel("UC-count-pending-1", Base.AddDays(1)),
            PendingChannel("UC-count-pending-2", Base.AddDays(2)),
            deletedPending,
            ApprovedChannel("UC-count-approved", Base.AddDays(3)),
            RejectedChannel("UC-count-rejected", Base.AddDays(4), "Not short-form craft"));

        var client = application.CreateClient();
        var counts = await client.GetFromJsonAsync<ReviewCountsPayload>("/api/channels/review-counts");

        Assert.NotNull(counts);
        Assert.Equal(2, counts.Pending);
        Assert.Equal(1, counts.Approved);
        Assert.Equal(1, counts.Rejected);
    }

    private static async Task<T?> ListAsync<T>(
        HttpClient client,
        string status,
        int page,
        int pageSize,
        string? searchText = null) =>
        await (await client.PostAsJsonAsync(
                $"/api/channels/{status}/list",
                new { page, pageSize, searchText }))
            .Content.ReadFromJsonAsync<T>();

    private static Channel PendingChannel(string youtubeChannelId, DateTimeOffset discoveredAt) => new()
    {
        Id = Guid.NewGuid(),
        YoutubeChannelId = youtubeChannelId,
        Name = $"Channel {youtubeChannelId}",
        ThumbnailUrl = $"https://example.com/{youtubeChannelId}.jpg",
        Status = ChannelStatus.PENDING,
        BackfillStatus = BackfillStatus.PENDING,
        IsDeleted = false,
        DiscoveredAt = discoveredAt,
        CreatedAt = discoveredAt,
        ModifiedAt = discoveredAt
    };

    private static Channel ApprovedChannel(string youtubeChannelId, DateTimeOffset approvedAt)
    {
        var channel = PendingChannel(youtubeChannelId, approvedAt.AddDays(-2));
        channel.Status = ChannelStatus.APPROVED;
        channel.ApprovedAt = approvedAt;
        channel.BackfillStatus = BackfillStatus.RUNNING;
        channel.LastUploadCheckAt = null;
        channel.ModifiedAt = approvedAt;
        return channel;
    }

    private static Channel RejectedChannel(string youtubeChannelId, DateTimeOffset rejectedAt, string reason)
    {
        var channel = PendingChannel(youtubeChannelId, rejectedAt.AddDays(-2));
        channel.Status = ChannelStatus.REJECTED;
        channel.RejectedAt = rejectedAt;
        channel.RejectionReason = reason;
        channel.ModifiedAt = rejectedAt;
        return channel;
    }

    private static async Task SeedAsync(ChannelApiFactory factory, params Channel[] channels)
    {
        using var scope = factory.Services.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<YtFetcherDbContext>();
        dbContext.Channels.AddRange(channels);
        await dbContext.SaveChangesAsync();
    }

    private sealed record ApprovedListPayload(
        IReadOnlyList<ApprovedItemPayload> Items,
        int Page,
        int PageSize,
        int TotalCount);

    private sealed record ApprovedItemPayload(
        Guid Id,
        string YoutubeChannelId,
        string Name,
        string? ThumbnailUrl,
        DateTimeOffset DiscoveredAt,
        DateTimeOffset? ApprovedAt,
        string BackfillStatus,
        DateTimeOffset? LastUploadCheckAt);

    private sealed record RejectedListPayload(
        IReadOnlyList<RejectedItemPayload> Items,
        int Page,
        int PageSize,
        int TotalCount);

    private sealed record RejectedItemPayload(
        Guid Id,
        string YoutubeChannelId,
        string Name,
        string? ThumbnailUrl,
        DateTimeOffset DiscoveredAt,
        DateTimeOffset? RejectedAt,
        string? RejectionReason);

    private sealed record ReviewCountsPayload(int Pending, int Approved, int Rejected);
}
