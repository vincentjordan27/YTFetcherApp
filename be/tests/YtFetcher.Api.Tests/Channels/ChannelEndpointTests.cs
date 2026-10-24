using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using System.Net.Http.Json;
using System.Net;
using YtFetcher.Core.Channels;
using YtFetcher.Core.Discovery;
using YtFetcher.Core.Users;
using YtFetcher.Application.Quotas;
using YtFetcher.Infrastructure.Persistence;

namespace YtFetcher.Api.Tests.Channels;

[Collection(ApiStartupCollection.Name)]
public class ChannelEndpointTests
{
    private const string RejectReason = "Irrelevant content for this niche";

    private static readonly DateTimeOffset SeedTime = new(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task Pending_list_returns_only_non_deleted_pending_channels_newest_first()
    {
        using var databaseConfiguration = new HostConfigurationScope();
        using var application = new ChannelApiFactory();

        var oldest = PendingChannel("UC-oldest", SeedTime);
        var newest = PendingChannel("UC-newest", SeedTime.AddDays(60));
        var approved = PendingChannel("UC-approved", SeedTime.AddDays(30));
        approved.Status = ChannelStatus.APPROVED;
        approved.ApprovedAt = approved.DiscoveredAt;
        var rejected = PendingChannel("UC-rejected", SeedTime.AddDays(31));
        rejected.Status = ChannelStatus.REJECTED;
        rejected.RejectedAt = rejected.DiscoveredAt;
        var deleted = PendingChannel("UC-deleted", SeedTime.AddDays(61));
        deleted.IsDeleted = true;
        deleted.DeletedAt = deleted.DiscoveredAt;

        await SeedAsync(application, oldest, newest, approved, rejected, deleted);

        var client = application.CreateClient();
        var response = await PostPendingListAsync(client, 1, 20);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var payload = await response.Content.ReadFromJsonAsync<PendingChannelsResponse>();

        Assert.NotNull(payload);
        Assert.Equal(1, payload.Page);
        Assert.Equal(20, payload.PageSize);
        Assert.Equal(2, payload.TotalCount);
        Assert.Collection(
            payload.Items,
            first => Assert.Equal(newest.Id, first.Id),
            second => Assert.Equal(oldest.Id, second.Id));
        Assert.All(payload.Items, item => Assert.Equal(0, item.EvidenceCount));
    }

    [Fact]
    public async Task Pending_pagination_uses_defaults_and_rejects_invalid_values()
    {
        using var databaseConfiguration = new HostConfigurationScope();
        using var application = new ChannelApiFactory();

        await SeedAsync(
            application,
            PendingChannel("UC-page-1", SeedTime),
            PendingChannel("UC-page-2", SeedTime.AddDays(1)),
            PendingChannel("UC-page-3", SeedTime.AddDays(2)));

        var client = application.CreateClient();

        var defaultResponse = await PostPendingListAsync(client, 1, 20);
        var defaultPayload = await defaultResponse.Content.ReadFromJsonAsync<PendingChannelsResponse>();
        Assert.Equal(HttpStatusCode.OK, defaultResponse.StatusCode);
        Assert.NotNull(defaultPayload);
        Assert.Equal(1, defaultPayload.Page);
        Assert.Equal(20, defaultPayload.PageSize);
        Assert.Equal(3, defaultPayload.TotalCount);
        Assert.Equal(3, defaultPayload.Items.Count);

        var pagedResponse = await PostPendingListAsync(client, 2, 1);
        var pagedPayload = await pagedResponse.Content.ReadFromJsonAsync<PendingChannelsResponse>();
        Assert.Equal(HttpStatusCode.OK, pagedResponse.StatusCode);
        Assert.NotNull(pagedPayload);
        Assert.Equal(2, pagedPayload.Page);
        Assert.Equal(1, pagedPayload.PageSize);
        Assert.Equal(3, pagedPayload.TotalCount);
        Assert.Single(pagedPayload.Items);

        Assert.Equal(HttpStatusCode.BadRequest, (await PostPendingListAsync(client, 0, 20)).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await PostPendingListAsync(client, 1, 0)).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await PostPendingListAsync(client, 1, 101)).StatusCode);
    }

    [Fact]
    public async Task Pending_search_matches_name_or_channel_id_case_insensitively_and_filters_before_paging()
    {
        using var databaseConfiguration = new HostConfigurationScope();
        using var application = new ChannelApiFactory();

        var kyoto = PendingChannel("UC-kyoto-craft", SeedTime.AddDays(3));
        kyoto.Name = "Kyoto Craft Journal";
        var kyotoSecond = PendingChannel("UC-kyoto-clay", SeedTime.AddDays(2));
        kyotoSecond.Name = "Kyoto Clay Studio";
        var mashiko = PendingChannel("UC-mashiko-works", SeedTime.AddDays(1));
        mashiko.Name = "Mashiko Pottery Works";

        await SeedAsync(application, kyoto, kyotoSecond, mashiko);

        var client = application.CreateClient();

        var lowerCaseName = await ListPendingAsync(client, searchText: "kyoto");
        Assert.NotNull(lowerCaseName);
        Assert.Equal(2, lowerCaseName.TotalCount);
        Assert.Equal([kyoto.Id, kyotoSecond.Id], lowerCaseName.Items.Select(item => item.Id));

        var upperCaseName = await ListPendingAsync(client, searchText: "KYOTO");
        Assert.NotNull(upperCaseName);
        Assert.Equal(2, upperCaseName.TotalCount);

        var byChannelId = await ListPendingAsync(client, searchText: "uc-MASHIKO");
        Assert.NotNull(byChannelId);
        Assert.Equal(1, byChannelId.TotalCount);
        Assert.Equal(mashiko.Id, byChannelId.Items.Single().Id);

        var trimmed = await ListPendingAsync(client, searchText: "  kyoto  ");
        Assert.NotNull(trimmed);
        Assert.Equal(2, trimmed.TotalCount);

        var filteredPage = await ListPendingAsync(client, page: 1, pageSize: 1, searchText: "kyoto");
        Assert.NotNull(filteredPage);
        Assert.Equal(2, filteredPage.TotalCount);
        Assert.Equal(1, filteredPage.PageSize);
        Assert.Equal(kyoto.Id, filteredPage.Items.Single().Id);

        var secondFilteredPage = await ListPendingAsync(client, page: 2, pageSize: 1, searchText: "kyoto");
        Assert.NotNull(secondFilteredPage);
        Assert.Equal(2, secondFilteredPage.TotalCount);
        Assert.Equal(kyotoSecond.Id, secondFilteredPage.Items.Single().Id);

        var blankQuery = await ListPendingAsync(client, searchText: "  ");
        Assert.NotNull(blankQuery);
        Assert.Equal(3, blankQuery.TotalCount);

        var noMatch = await ListPendingAsync(client, searchText: "osaka");
        Assert.NotNull(noMatch);
        Assert.Equal(0, noMatch.TotalCount);
        Assert.Empty(noMatch.Items);
    }

    [Fact]
    public async Task Pending_list_counts_only_non_deleted_evidence()
    {
        using var databaseConfiguration = new HostConfigurationScope();
        using var application = new ChannelApiFactory();

        var channel = PendingChannel("UC-evidence-count", SeedTime);
        var keyword = Keyword("craft shorts");
        var evidence = new[]
        {
            Evidence(channel, keyword, "video-1", SeedTime.AddDays(1)),
            Evidence(channel, keyword, "video-2", SeedTime.AddDays(2)),
            Evidence(channel, keyword, "video-3", SeedTime.AddDays(3))
        };
        evidence[2].IsDeleted = true;

        await SeedAsync(application, channel);
        await SeedEvidenceAsync(application, [keyword], evidence);

        var client = application.CreateClient();
        var pending = await ListPendingAsync(client);

        Assert.NotNull(pending);
        var item = Assert.Single(pending.Items);
        Assert.Equal(2, item.EvidenceCount);
    }

    [Fact]
    public async Task Evidence_returns_at_most_five_newest_rows_with_their_keyword()
    {
        using var databaseConfiguration = new HostConfigurationScope();
        using var application = new ChannelApiFactory();

        var channel = PendingChannel("UC-evidence-cap", SeedTime);
        var firstKeyword = Keyword("sakura city");
        var secondKeyword = Keyword("kota sakura");
        var samePublishedAt = SeedTime.AddDays(5);
        var higherId = Evidence(channel, firstKeyword, "video-tie-high", samePublishedAt);
        var lowerId = Evidence(channel, firstKeyword, "video-tie-low", samePublishedAt);
        higherId.Id = Guid.Parse("ffffffff-ffff-ffff-ffff-ffffffffffff");
        lowerId.Id = Guid.Parse("00000000-0000-0000-0000-000000000001");
        higherId.VideoTitle = "Tie high";

        var evidence = new[]
        {
            Evidence(channel, firstKeyword, "video-oldest", SeedTime.AddDays(1)),
            lowerId,
            higherId,
            Evidence(channel, secondKeyword, "video-middle", SeedTime.AddDays(3)),
            Evidence(channel, secondKeyword, "video-newest", SeedTime.AddDays(9)),
            Evidence(channel, firstKeyword, "video-hidden", SeedTime.AddDays(0))
        };

        await SeedAsync(application, channel);
        await SeedEvidenceAsync(application, [firstKeyword, secondKeyword], evidence);

        var client = application.CreateClient();
        var response = await client.GetAsync($"/api/channels/{channel.Id}/evidence");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var payload = await response.Content.ReadFromJsonAsync<ChannelEvidenceResponse>();
        Assert.NotNull(payload);
        Assert.Equal(5, payload.Items.Count);
        Assert.Equal(
            ["video-newest", "video-tie-high", "video-tie-low", "video-middle", "video-oldest"],
            payload.Items.Select(item => item.YouTubeVideoId));

        var newest = payload.Items[0];
        Assert.Equal("Video video-newest", newest.Title);
        Assert.Equal($"https://example.com/video-newest.jpg", newest.ThumbnailUrl);
        Assert.Equal(SeedTime.AddDays(9), newest.PublishedAt);
        Assert.Equal("kota sakura", newest.Keyword);

        var tieHigh = payload.Items[1];
        Assert.Equal("Tie high", tieHigh.Title);
        Assert.Equal("sakura city", tieHigh.Keyword);
    }

    [Fact]
    public async Task Evidence_ignores_soft_deleted_rows_and_returns_an_empty_list_without_evidence()
    {
        using var databaseConfiguration = new HostConfigurationScope();
        using var application = new ChannelApiFactory();

        var channel = PendingChannel("UC-evidence-empty", SeedTime);
        var keyword = Keyword("sakura city");
        var deleted = Evidence(channel, keyword, "video-deleted", SeedTime.AddDays(1));
        deleted.IsDeleted = true;

        await SeedAsync(application, channel);
        await SeedEvidenceAsync(application, [keyword], [deleted]);

        var client = application.CreateClient();
        var response = await client.GetAsync($"/api/channels/{channel.Id}/evidence");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var payload = await response.Content.ReadFromJsonAsync<ChannelEvidenceResponse>();
        Assert.NotNull(payload);
        Assert.Empty(payload.Items);
    }

    [Fact]
    public async Task Evidence_returns_not_found_for_missing_and_soft_deleted_channels()
    {
        using var databaseConfiguration = new HostConfigurationScope();
        using var application = new ChannelApiFactory();

        var deleted = PendingChannel("UC-evidence-deleted", SeedTime);
        deleted.IsDeleted = true;
        deleted.DeletedAt = SeedTime;
        await SeedAsync(application, deleted);

        var client = application.CreateClient();

        Assert.Equal(HttpStatusCode.NotFound, (await client.GetAsync($"/api/channels/{Guid.NewGuid()}/evidence")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await client.GetAsync($"/api/channels/{deleted.Id}/evidence")).StatusCode);

        var invalidResponse = await client.GetAsync("/api/channels/not-a-guid/evidence");
        Assert.Equal(HttpStatusCode.BadRequest, invalidResponse.StatusCode);
        var problem = await invalidResponse.Content.ReadFromJsonAsync<ValidationProblemResponse>();
        Assert.NotNull(problem);
        Assert.Contains("channelId", problem.Errors.Keys);
    }

    [Fact]
    public async Task Detail_returns_not_found_for_missing_and_soft_deleted_channels()
    {
        using var databaseConfiguration = new HostConfigurationScope();
        using var application = new ChannelApiFactory();

        var deleted = PendingChannel("UC-detail-deleted", SeedTime);
        deleted.IsDeleted = true;
        deleted.DeletedAt = SeedTime;
        await SeedAsync(application, deleted);

        var client = application.CreateClient();

        Assert.Equal(HttpStatusCode.NotFound, (await client.GetAsync($"/api/channels/{Guid.NewGuid()}")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await client.GetAsync($"/api/channels/{deleted.Id}")).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await client.GetAsync("/api/channels/not-a-guid")).StatusCode);
    }

    [Fact]
    public async Task Approve_transitions_pending_channel_and_sets_audit_timestamps()
    {
        using var databaseConfiguration = new HostConfigurationScope();
        using var application = new ChannelApiFactory();

        var channel = PendingChannel("UC-approve", SeedTime);
        await SeedAsync(application, channel);

        var client = application.CreateClient();
        var response = await client.PostAsJsonAsync($"/api/channels/{channel.Id}/approve", new { });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var detail = await response.Content.ReadFromJsonAsync<ChannelDetailResponse>();
        Assert.NotNull(detail);
        Assert.Equal("APPROVED", detail.Status);
        Assert.Equal("SCHEDULING", detail.BackfillStatus);
        Assert.NotNull(detail.ApprovedAt);
        Assert.Null(detail.RejectedAt);

        var stored = await ReadChannelAsync(application, channel.Id);
        Assert.Equal(ChannelStatus.APPROVED, stored.Status);
        Assert.NotNull(stored.ApprovedAt);
        Assert.Equal(User.SystemUserId, stored.ApprovedByUserId);
        Assert.Equal(BackfillStatus.SCHEDULING, stored.BackfillStatus);
        Assert.True(stored.ModifiedAt > channel.ModifiedAt);
    }

    [Fact]
    public async Task Bulk_approve_rejects_the_entire_selection_when_the_daily_limit_has_insufficient_capacity()
    {
        using var databaseConfiguration = new HostConfigurationScope();
        using var application = new ChannelApiFactory();

        var channels = Enumerable.Range(1, 4)
            .Select(index => PendingChannel($"UC-bulk-limit-{index}", SeedTime.AddMinutes(index)))
            .ToArray();
        await SeedAsync(application, channels);

        using (var scope = application.Services.CreateScope())
        {
            var dbContext = scope.ServiceProvider.GetRequiredService<YtFetcherDbContext>();
            var quotaDate = PacificQuotaWindow.GetQuotaDate(DateTimeOffset.UtcNow);
            var ledger = await dbContext.YouTubeDailyQuotaUsages.SingleAsync(usage => usage.QuotaDate == quotaDate);
            ledger.FirstPageAttemptLimit = 100;
            ledger.FirstPageAttemptUsed = 97;
            ledger.QueriesUnitsUsed = 97;
            ledger.VideoBatchStatsUnitsUsed = 97;
            await dbContext.SaveChangesAsync();
        }

        var client = application.CreateClient();
        var response = await client.PostAsJsonAsync("/api/channels/approve", new { channelIds = channels.Select(channel => channel.Id) });

        Assert.Equal(HttpStatusCode.TooManyRequests, response.StatusCode);

        foreach (var channel in channels)
        {
            Assert.Equal(ChannelStatus.PENDING, (await ReadChannelAsync(application, channel.Id)).Status);
        }

        using var verificationScope = application.Services.CreateScope();
        var verificationContext = verificationScope.ServiceProvider.GetRequiredService<YtFetcherDbContext>();
        var verificationLedger = await verificationContext.YouTubeDailyQuotaUsages.SingleAsync(usage => usage.QuotaDate == PacificQuotaWindow.GetQuotaDate(DateTimeOffset.UtcNow));
        Assert.Equal(97, verificationLedger.FirstPageAttemptUsed);
        Assert.Equal(97, verificationLedger.QueriesUnitsUsed);
        Assert.Equal(97, verificationLedger.VideoBatchStatsUnitsUsed);
    }

    [Fact]
    public async Task Reject_transitions_pending_channel_stores_the_trimmed_reason_and_sets_audit_timestamps()
    {
        using var databaseConfiguration = new HostConfigurationScope();
        using var application = new ChannelApiFactory();

        var channel = PendingChannel("UC-reject", SeedTime);
        await SeedAsync(application, channel);

        var client = application.CreateClient();
        var response = await client.PostAsJsonAsync(
            $"/api/channels/{channel.Id}/reject",
            new { reason = $"   {RejectReason}   " });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var detail = await response.Content.ReadFromJsonAsync<ChannelDetailResponse>();
        Assert.NotNull(detail);
        Assert.Equal("REJECTED", detail.Status);
        Assert.NotNull(detail.RejectedAt);
        Assert.Null(detail.ApprovedAt);

        var stored = await ReadChannelAsync(application, channel.Id);
        Assert.Equal(ChannelStatus.REJECTED, stored.Status);
        Assert.NotNull(stored.RejectedAt);
        Assert.Equal(User.SystemUserId, stored.RejectedByUserId);
        Assert.Equal(RejectReason, stored.RejectionReason);
        Assert.True(stored.ModifiedAt > channel.ModifiedAt);
    }

    [Fact]
    public async Task Bulk_reject_leaves_every_selected_channel_unchanged_when_one_channel_is_already_rejected()
    {
        using var databaseConfiguration = new HostConfigurationScope();
        using var application = new ChannelApiFactory();

        var pending = PendingChannel("UC-bulk-reject-pending", SeedTime);
        var rejected = PendingChannel("UC-bulk-reject-existing", SeedTime.AddMinutes(1));
        rejected.Status = ChannelStatus.REJECTED;
        rejected.RejectedAt = SeedTime.AddMinutes(1);
        rejected.RejectionReason = "Already rejected";
        await SeedAsync(application, pending, rejected);

        var client = application.CreateClient();
        var response = await client.PostAsJsonAsync(
            "/api/channels/reject",
            new { channelIds = new[] { pending.Id, rejected.Id }, reason = RejectReason });

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        var storedPending = await ReadChannelAsync(application, pending.Id);
        Assert.Equal(ChannelStatus.PENDING, storedPending.Status);
        Assert.Null(storedPending.RejectedAt);
        Assert.Null(storedPending.RejectionReason);
    }

    [Fact]
    public async Task Reject_requires_a_reason_within_the_column_limit()
    {
        using var databaseConfiguration = new HostConfigurationScope();
        using var application = new ChannelApiFactory();

        var channel = PendingChannel("UC-reject-validation", SeedTime);
        await SeedAsync(application, channel);

        var client = application.CreateClient();

        var missing = await client.PostAsJsonAsync($"/api/channels/{channel.Id}/reject", new { });
        Assert.Equal(HttpStatusCode.BadRequest, missing.StatusCode);
        var missingProblem = await missing.Content.ReadFromJsonAsync<ValidationProblemResponse>();
        Assert.NotNull(missingProblem);
        Assert.Contains("reason", missingProblem.Errors.Keys);

        var blank = await client.PostAsJsonAsync($"/api/channels/{channel.Id}/reject", new { reason = "   " });
        Assert.Equal(HttpStatusCode.BadRequest, blank.StatusCode);
        var blankProblem = await blank.Content.ReadFromJsonAsync<ValidationProblemResponse>();
        Assert.NotNull(blankProblem);
        Assert.Contains("reason", blankProblem.Errors.Keys);

        var overlong = await client.PostAsJsonAsync(
            $"/api/channels/{channel.Id}/reject",
            new { reason = new string('a', Channel.RejectionReasonMaxLength + 1) });
        Assert.Equal(HttpStatusCode.BadRequest, overlong.StatusCode);
        var overlongProblem = await overlong.Content.ReadFromJsonAsync<ValidationProblemResponse>();
        Assert.NotNull(overlongProblem);
        Assert.Contains("reason", overlongProblem.Errors.Keys);

        var stored = await ReadChannelAsync(application, channel.Id);
        Assert.Equal(ChannelStatus.PENDING, stored.Status);
        Assert.Null(stored.RejectionReason);
    }

    [Fact]
    public async Task Approve_or_reject_returns_conflict_for_non_pending_channels()
    {
        using var databaseConfiguration = new HostConfigurationScope();
        using var application = new ChannelApiFactory();

        var approved = PendingChannel("UC-conflict-approved", SeedTime);
        approved.Status = ChannelStatus.APPROVED;
        approved.ApprovedAt = approved.DiscoveredAt;
        var rejected = PendingChannel("UC-conflict-rejected", SeedTime.AddDays(1));
        rejected.Status = ChannelStatus.REJECTED;
        rejected.RejectedAt = rejected.DiscoveredAt;
        await SeedAsync(application, approved, rejected);

        var client = application.CreateClient();
        var rejectionBody = new { reason = RejectReason };

        var approveApproved = await client.PostAsJsonAsync($"/api/channels/{approved.Id}/approve", new { });
        Assert.Equal(HttpStatusCode.Conflict, approveApproved.StatusCode);
        var conflict = await approveApproved.Content.ReadFromJsonAsync<ConflictResponse>();
        Assert.NotNull(conflict);
        Assert.Equal("APPROVED", conflict.CurrentStatus);

        // Rejecting an approved channel is now a valid lifecycle move; the conflict set below covers
        // the remaining invalid sources only.
        Assert.Equal(HttpStatusCode.Conflict, (await client.PostAsJsonAsync($"/api/channels/{rejected.Id}/approve", new { })).StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, (await client.PostAsJsonAsync($"/api/channels/{rejected.Id}/reject", rejectionBody)).StatusCode);

        var storedRejected = await ReadChannelAsync(application, rejected.Id);
        Assert.Null(storedRejected.RejectionReason);

        var pending = await ListPendingAsync(client);
        Assert.NotNull(pending);
        Assert.Equal(0, pending.TotalCount);
        Assert.DoesNotContain(pending.Items, item => item.Id == approved.Id || item.Id == rejected.Id);
    }

    [Fact]
    public async Task Reject_soft_deleted_channel_returns_not_found_and_does_not_mutate_it()
    {
        using var databaseConfiguration = new HostConfigurationScope();
        using var application = new ChannelApiFactory();

        var channel = PendingChannel("UC-deleted-reject", SeedTime);
        channel.Status = ChannelStatus.APPROVED;
        channel.ApprovedAt = SeedTime.AddHours(1);
        channel.IsDeleted = true;
        channel.DeletedAt = SeedTime.AddHours(2);
        await SeedAsync(application, channel);

        var client = application.CreateClient();
        var response = await client.PostAsJsonAsync(
            $"/api/channels/{channel.Id}/reject",
            new { reason = "Not a fit" });

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);

        var stored = await ReadChannelAsync(application, channel.Id);
        Assert.Equal(ChannelStatus.APPROVED, stored.Status);
        Assert.True(stored.IsDeleted);
        Assert.Null(stored.RejectionReason);
        Assert.Null(stored.RejectedAt);
    }

    [Fact]
    public async Task Reject_approved_channel_moves_it_to_rejected_and_preserves_backfill_state()
    {
        using var databaseConfiguration = new HostConfigurationScope();
        using var application = new ChannelApiFactory();

        var channel = PendingChannel("UC-approved-reject", SeedTime);
        channel.Status = ChannelStatus.APPROVED;
        channel.ApprovedAt = SeedTime.AddHours(1);
        channel.BackfillStatus = BackfillStatus.RUNNING;
        channel.BackfillPageToken = "next-page";
        channel.BackfillClaimToken = Guid.NewGuid();
        channel.BackfillClaimedAt = SeedTime.AddHours(2);
        channel.BackfillCutoffAt = SeedTime.AddDays(-365);
        await SeedAsync(application, channel);

        var client = application.CreateClient();
        var response = await client.PostAsJsonAsync(
            $"/api/channels/{channel.Id}/reject",
            new { reason = "Not a fit" });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var detail = await response.Content.ReadFromJsonAsync<ChannelDetailResponse>();
        Assert.NotNull(detail);
        Assert.Equal("REJECTED", detail.Status);
        Assert.Equal("RUNNING", detail.BackfillStatus);

        var stored = await ReadChannelAsync(application, channel.Id);
        Assert.Equal(ChannelStatus.REJECTED, stored.Status);
        Assert.Equal("Not a fit", stored.RejectionReason);
        Assert.NotNull(stored.RejectedAt);

        // The existing backfill claim/progress is intentionally preserved.
        Assert.Equal(BackfillStatus.RUNNING, stored.BackfillStatus);
        Assert.Equal("next-page", stored.BackfillPageToken);
        Assert.Equal(channel.BackfillClaimToken, stored.BackfillClaimToken);
        Assert.Equal(channel.BackfillClaimedAt, stored.BackfillClaimedAt);
        Assert.Equal(channel.BackfillCutoffAt, stored.BackfillCutoffAt);
    }

    [Fact]
    public async Task Reopen_rejected_completed_channel_returns_it_to_pending_without_erasing_backfill_history()
    {
        using var databaseConfiguration = new HostConfigurationScope();
        using var application = new ChannelApiFactory();

        var channel = PendingChannel("UC-reopen-completed", SeedTime);
        channel.Status = ChannelStatus.REJECTED;
        channel.RejectedAt = SeedTime.AddHours(1);
        channel.RejectionReason = "No longer a fit";
        channel.BackfillStatus = BackfillStatus.COMPLETED;
        channel.BackfillPagesCompleted = 3;
        channel.BackfillCutoffAt = SeedTime.AddDays(-Channel.BackfillHorizonDays);
        await SeedAsync(application, channel);

        var client = application.CreateClient();
        var response = await client.PostAsJsonAsync($"/api/channels/{channel.Id}/reopen", new { });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var detail = await response.Content.ReadFromJsonAsync<ChannelDetailResponse>();
        Assert.NotNull(detail);
        Assert.Equal("PENDING", detail.Status);
        Assert.Equal("COMPLETED", detail.BackfillStatus);

        var stored = await ReadChannelAsync(application, channel.Id);
        Assert.Equal(ChannelStatus.PENDING, stored.Status);
        Assert.Null(stored.RejectedAt);
        Assert.Null(stored.RejectionReason);
        Assert.Equal(BackfillStatus.COMPLETED, stored.BackfillStatus);
        Assert.Equal(3, stored.BackfillPagesCompleted);
        Assert.Equal(SeedTime.AddDays(-Channel.BackfillHorizonDays), stored.BackfillCutoffAt);
    }

    [Fact]
    public async Task Reopen_rejected_channel_returns_conflict_while_its_backfill_page_is_running()
    {
        using var databaseConfiguration = new HostConfigurationScope();
        using var application = new ChannelApiFactory();

        var channel = PendingChannel("UC-reopen-running", SeedTime);
        channel.Status = ChannelStatus.REJECTED;
        channel.RejectedAt = SeedTime.AddHours(1);
        channel.RejectionReason = "No longer a fit";
        channel.BackfillStatus = BackfillStatus.RUNNING;
        channel.BackfillClaimToken = Guid.NewGuid();
        channel.BackfillClaimedAt = SeedTime.AddHours(1);
        await SeedAsync(application, channel);

        var client = application.CreateClient();
        var response = await client.PostAsJsonAsync($"/api/channels/{channel.Id}/reopen", new { });

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        var problem = await response.Content.ReadFromJsonAsync<ConflictResponse>();
        Assert.NotNull(problem);
        Assert.Equal("REJECTED", problem.CurrentStatus);

        var stored = await ReadChannelAsync(application, channel.Id);
        Assert.Equal(ChannelStatus.REJECTED, stored.Status);
        Assert.Equal(BackfillStatus.RUNNING, stored.BackfillStatus);
    }

    [Fact]
    public async Task Delete_soft_deletes_channel_without_removing_the_row()
    {
        using var databaseConfiguration = new HostConfigurationScope();
        using var application = new ChannelApiFactory();

        var channel = PendingChannel("UC-delete", SeedTime);
        await SeedAsync(application, channel);

        var client = application.CreateClient();
        var response = await DeleteWithBodyAsync(client, $"/api/channels/{channel.Id}");

        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await client.GetAsync($"/api/channels/{channel.Id}")).StatusCode);

        var pending = await ListPendingAsync(client);
        Assert.NotNull(pending);
        Assert.DoesNotContain(pending.Items, item => item.Id == channel.Id);

        var stored = await ReadChannelAsync(application, channel.Id);
        Assert.True(stored.IsDeleted);
        Assert.NotNull(stored.DeletedAt);
        Assert.Equal(User.SystemUserId, stored.DeletedByUserId);
        Assert.True(stored.ModifiedAt > channel.ModifiedAt);

        Assert.Equal(HttpStatusCode.NotFound, (await DeleteWithBodyAsync(client, $"/api/channels/{channel.Id}")).StatusCode);
    }

    [Fact]
    public async Task Successful_mutations_emit_one_structured_log_with_required_fields()
    {
        using var databaseConfiguration = new HostConfigurationScope();
        using var application = new ChannelApiFactory();

        var approveChannel = PendingChannel("UC-log-approve", SeedTime);
        var rejectChannel = PendingChannel("UC-log-reject", SeedTime.AddDays(1));
        var deleteChannel = PendingChannel("UC-log-delete", SeedTime.AddDays(2));
        await SeedAsync(application, approveChannel, rejectChannel, deleteChannel);

        var client = application.CreateClient();
        Assert.Equal(HttpStatusCode.OK, (await client.PostAsJsonAsync($"/api/channels/{approveChannel.Id}/approve", new { })).StatusCode);
        Assert.Equal(
            HttpStatusCode.OK,
            (await client.PostAsJsonAsync($"/api/channels/{rejectChannel.Id}/reject", new { reason = RejectReason })).StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, (await DeleteWithBodyAsync(client, $"/api/channels/{deleteChannel.Id}")).StatusCode);

        var entries = application.LogSink.Entries;

        var approved = Assert.Single(entries, entry => entry.Message.StartsWith("ChannelApproved", StringComparison.Ordinal));
        Assert.Equal(LogLevel.Information, approved.Level);
        Assert.Equal(approveChannel.Id.ToString(), approved.Properties["ChannelId"]?.ToString());
        Assert.Equal("UC-log-approve", approved.Properties["YouTubeChannelId"]);
        Assert.Equal("PENDING", approved.Properties["PreviousStatus"]?.ToString());
        Assert.Equal("APPROVED", approved.Properties["CurrentStatus"]?.ToString());
        // The approval log records the durable post-approval state, which is SCHEDULING until the
        // enqueue result is persisted.
        Assert.Equal("SCHEDULING", approved.Properties["BackfillStatus"]?.ToString());

        var rejected = Assert.Single(entries, entry => entry.Message.StartsWith("ChannelRejected", StringComparison.Ordinal));
        Assert.Equal(LogLevel.Information, rejected.Level);
        Assert.Equal(rejectChannel.Id.ToString(), rejected.Properties["ChannelId"]?.ToString());
        Assert.Equal("UC-log-reject", rejected.Properties["YouTubeChannelId"]);
        Assert.Equal("PENDING", rejected.Properties["PreviousStatus"]?.ToString());
        Assert.Equal("REJECTED", rejected.Properties["CurrentStatus"]?.ToString());

        var deleted = Assert.Single(entries, entry => entry.Message.StartsWith("ChannelSoftDeleted", StringComparison.Ordinal));
        Assert.Equal(LogLevel.Information, deleted.Level);
        Assert.Equal(deleteChannel.Id.ToString(), deleted.Properties["ChannelId"]?.ToString());
        Assert.Equal("UC-log-delete", deleted.Properties["YouTubeChannelId"]);
        Assert.Equal("PENDING", deleted.Properties["PreviousStatus"]?.ToString());

        Assert.DoesNotContain(entries, entry => entry.Message.Contains("test-password", StringComparison.OrdinalIgnoreCase));
        Assert.DoesNotContain(
            entries,
            entry => entry.Message.Contains(RejectReason, StringComparison.OrdinalIgnoreCase) ||
                     entry.Properties.Values.Any(value =>
                         value?.ToString()?.Contains(RejectReason, StringComparison.OrdinalIgnoreCase) == true));
    }

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

    private static DiscoveryKeyword Keyword(string value) => new()
    {
        Id = Guid.NewGuid(),
        Keyword = value,
        IsActive = true,
        IsDeleted = false,
        CreatedAt = SeedTime,
        ModifiedAt = SeedTime
    };

    private static ChannelDiscovery Evidence(
        Channel channel,
        DiscoveryKeyword keyword,
        string youtubeVideoId,
        DateTimeOffset publishedAt) => new()
    {
        Id = Guid.NewGuid(),
        ChannelId = channel.Id,
        KeywordId = keyword.Id,
        YoutubeVideoId = youtubeVideoId,
        VideoTitle = $"Video {youtubeVideoId}",
        VideoThumbnailUrl = $"https://example.com/{youtubeVideoId}.jpg",
        VideoPublishedAt = publishedAt,
        DiscoveredAt = SeedTime,
        IsDeleted = false,
        CreatedAt = SeedTime,
        ModifiedAt = SeedTime
    };

    /// <summary>
    /// The paged list is a body-based action: page, page size, and the optional search text travel in
    /// the JSON body instead of the query string.
    /// </summary>
    private static Task<HttpResponseMessage> PostPendingListAsync(
        HttpClient client,
        int page,
        int pageSize,
        string? searchText = null) =>
        client.PostAsJsonAsync("/api/channels/pending/list", new { page, pageSize, searchText });

    private static async Task<PendingChannelsResponse?> ListPendingAsync(
        HttpClient client,
        int page = 1,
        int pageSize = 20,
        string? searchText = null) =>
        await (await PostPendingListAsync(client, page, pageSize, searchText))
            .Content.ReadFromJsonAsync<PendingChannelsResponse>();

    /// <summary>Soft delete is a non-GET action, so it sends an empty JSON body.</summary>
    private static Task<HttpResponseMessage> DeleteWithBodyAsync(HttpClient client, string url) =>
        client.SendAsync(new HttpRequestMessage(HttpMethod.Delete, url)
        {
            Content = JsonContent.Create(new { })
        });

    private static async Task SeedAsync(ChannelApiFactory factory, params Channel[] channels)
    {
        using var scope = factory.Services.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<YtFetcherDbContext>();
        dbContext.Channels.AddRange(channels);
        await dbContext.SaveChangesAsync();
    }

    private static async Task SeedEvidenceAsync(
        ChannelApiFactory factory,
        DiscoveryKeyword[] keywords,
        ChannelDiscovery[] discoveries)
    {
        using var scope = factory.Services.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<YtFetcherDbContext>();
        dbContext.DiscoveryKeywords.AddRange(keywords);
        dbContext.ChannelDiscoveries.AddRange(discoveries);
        await dbContext.SaveChangesAsync();
    }

    private static async Task<Channel> ReadChannelAsync(ChannelApiFactory factory, Guid channelId)
    {
        using var scope = factory.Services.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<YtFetcherDbContext>();
        return await dbContext.Channels.SingleAsync(channel => channel.Id == channelId);
    }

    private sealed record PendingChannelsResponse(
        IReadOnlyList<PendingChannelItemResponse> Items,
        int Page,
        int PageSize,
        int TotalCount);

    private sealed record PendingChannelItemResponse(
        Guid Id,
        string YouTubeChannelId,
        string Name,
        string? ThumbnailUrl,
        DateTimeOffset DiscoveredAt,
        int EvidenceCount);

    private sealed record ChannelEvidenceResponse(IReadOnlyList<ChannelEvidenceItemResponse> Items);

    private sealed record ChannelEvidenceItemResponse(
        string YouTubeVideoId,
        string Title,
        string? ThumbnailUrl,
        DateTimeOffset PublishedAt,
        string Keyword);

    private sealed record ChannelDetailResponse(
        Guid Id,
        string YouTubeChannelId,
        string Name,
        string? ThumbnailUrl,
        string Status,
        string? UploadsPlaylistId,
        string BackfillStatus,
        DateTimeOffset DiscoveredAt,
        DateTimeOffset? ApprovedAt,
        DateTimeOffset? RejectedAt,
        DateTimeOffset? LastUploadCheckAt,
        DateTimeOffset CreatedAt,
        DateTimeOffset ModifiedAt);

    private sealed record ConflictResponse(string? Title, string? Detail, string? CurrentStatus);

    private sealed record ValidationProblemResponse(Dictionary<string, string[]> Errors);
}
