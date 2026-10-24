using System.Net;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using YtFetcher.Api.Tests.Channels;
using YtFetcher.Core.BackfillRuns;
using YtFetcher.Core.Channels;
using YtFetcher.Infrastructure.Persistence;

namespace YtFetcher.Api.Tests.BackfillRuns;

/// <summary>
/// HTTP contract of the durable run history: the body-based paged list with its optional filters, the
/// single-run detail, and the exclusion of soft-deleted runs.
/// </summary>
[Collection(ApiStartupCollection.Name)]
public class BackfillRunEndpointTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 5, 9, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task The_list_returns_the_joined_channel_data_newest_first()
    {
        using var configuration = new HostConfigurationScope();
        using var application = new ChannelApiFactory();
        var channel = ApprovedChannel("Sakura Clips", "UC-sakura");
        var older = SucceededRun(channel.Id, 1, Now.AddDays(-3));
        var newer = FailedRun(channel.Id, 2, Now.AddDays(-1));
        await SeedAsync(application, channel, older, newer);
        using var client = application.CreateClient();

        var response = await client.PostAsJsonAsync("/api/backfill-runs/list", ListBody());

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var page = await response.Content.ReadFromJsonAsync<RunPagePayload>();
        Assert.NotNull(page);
        Assert.Equal(2, page.TotalCount);
        Assert.Equal(2, page.Items.Count);
        Assert.Equal(newer.Id, page.Items[0].Id);
        Assert.Equal("Sakura Clips", page.Items[0].ChannelName);
        Assert.Equal("UC-sakura", page.Items[0].YoutubeChannelId);
        Assert.Equal("FAILED", page.Items[0].Status);
        Assert.Equal("DAILY_PUMP", page.Items[0].TriggerSource);
        Assert.NotNull(page.Items[0].FailureReason);
        Assert.Equal("SUCCEEDED", page.Items[1].Status);

        // A succeeded run carries no failure detail at all.
        Assert.Null(page.Items[1].FailureReason);
        Assert.Null(page.Items[1].FailureCountAtTime);
    }

    [Fact]
    public async Task A_failed_run_exposes_its_readable_reason_and_failure_count()
    {
        using var configuration = new HostConfigurationScope();
        using var application = new ChannelApiFactory();
        var channel = ApprovedChannel("Sakura Clips", "UC-sakura");
        var run = FailedRun(channel.Id, 2, Now.AddDays(-1));
        await SeedAsync(application, channel, run);
        using var client = application.CreateClient();

        var response = await client.PostAsJsonAsync("/api/backfill-runs/list", ListBody());

        var page = await response.Content.ReadFromJsonAsync<RunPagePayload>();
        Assert.NotNull(page);
        var item = Assert.Single(page.Items);
        Assert.Equal("Unable to save backfill results. Try again later.", item.FailureReason);
        Assert.Equal(2, item.FailureCountAtTime);
        Assert.NotNull(item.CompletedAt);
    }

    [Fact]
    public async Task The_status_trigger_and_search_filters_apply()
    {
        using var configuration = new HostConfigurationScope();
        using var application = new ChannelApiFactory();
        var sakura = ApprovedChannel("Sakura Clips", "UC-sakura");
        var vtuber = ApprovedChannel("Vtuber Weekly", "UC-vtuber");
        await SeedAsync(application, sakura, SucceededRun(sakura.Id, 1, Now.AddDays(-3)));
        await SeedAsync(application, vtuber, FailedRun(vtuber.Id, 1, Now.AddDays(-1), BackfillTriggerSource.RECOVERY));
        using var client = application.CreateClient();

        var byStatus = await ReadPageAsync(client, new { page = 1, pageSize = 20, searchText = (string?)null, status = "FAILED" });
        var byTrigger = await ReadPageAsync(client, new { page = 1, pageSize = 20, searchText = (string?)null, triggerSource = "RECOVERY" });
        var bySearch = await ReadPageAsync(client, new { page = 1, pageSize = 20, searchText = "vtuber" });
        var byChannelId = await ReadPageAsync(client, new { page = 1, pageSize = 20, searchText = "UC-sakura" });

        Assert.Equal(1, byStatus.TotalCount);
        Assert.Equal("Vtuber Weekly", byStatus.Items[0].ChannelName);
        Assert.Equal(1, byTrigger.TotalCount);
        Assert.Equal("Vtuber Weekly", byTrigger.Items[0].ChannelName);
        Assert.Equal(1, bySearch.TotalCount);
        Assert.Equal(1, byChannelId.TotalCount);
        Assert.Equal("Sakura Clips", byChannelId.Items[0].ChannelName);
    }

    [Fact]
    public async Task The_date_window_is_inclusive_of_whole_utc_days()
    {
        using var configuration = new HostConfigurationScope();
        using var application = new ChannelApiFactory();
        var channel = ApprovedChannel("Sakura Clips", "UC-sakura");
        await SeedAsync(
            application,
            channel,
            SucceededRun(channel.Id, 1, new DateTimeOffset(2026, 9, 30, 23, 59, 59, TimeSpan.Zero)),
            SucceededRun(channel.Id, 2, new DateTimeOffset(2026, 10, 1, 0, 0, 0, TimeSpan.Zero)),
            SucceededRun(channel.Id, 3, new DateTimeOffset(2026, 10, 2, 23, 59, 59, TimeSpan.Zero)),
            SucceededRun(channel.Id, 4, new DateTimeOffset(2026, 10, 3, 0, 0, 0, TimeSpan.Zero)));
        using var client = application.CreateClient();

        var page = await ReadPageAsync(client, new
        {
            page = 1,
            pageSize = 20,
            searchText = (string?)null,
            dateFrom = "2026-10-01",
            dateTo = "2026-10-02"
        });

        Assert.Equal(2, page.TotalCount);
    }

    [Fact]
    public async Task A_soft_deleted_run_is_hidden_from_the_list_and_the_detail()
    {
        using var configuration = new HostConfigurationScope();
        using var application = new ChannelApiFactory();
        var channel = ApprovedChannel("Sakura Clips", "UC-sakura");
        var visible = SucceededRun(channel.Id, 1, Now.AddDays(-2));
        var deleted = SucceededRun(channel.Id, 2, Now.AddDays(-1));
        deleted.IsDeleted = true;
        await SeedAsync(application, channel, visible, deleted);
        using var client = application.CreateClient();

        var page = await ReadPageAsync(client, ListBody());

        Assert.Equal(1, page.TotalCount);
        Assert.Equal(visible.Id, page.Items[0].Id);
        Assert.Equal(HttpStatusCode.NotFound, (await client.GetAsync($"/api/backfill-runs/{deleted.Id}")).StatusCode);
    }

    [Fact]
    public async Task The_detail_returns_one_run_and_an_unknown_id_is_not_found()
    {
        using var configuration = new HostConfigurationScope();
        using var application = new ChannelApiFactory();
        var channel = ApprovedChannel("Sakura Clips", "UC-sakura");
        var run = FailedRun(channel.Id, 3, Now.AddDays(-1));
        await SeedAsync(application, channel, run);
        using var client = application.CreateClient();

        var response = await client.GetAsync($"/api/backfill-runs/{run.Id}");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var detail = await response.Content.ReadFromJsonAsync<RunPayload>();
        Assert.NotNull(detail);
        Assert.Equal(run.Id, detail.Id);
        Assert.Equal(3, detail.PageNumber);
        Assert.Equal("Sakura Clips", detail.ChannelName);

        Assert.Equal(
            HttpStatusCode.NotFound,
            (await client.GetAsync($"/api/backfill-runs/{Guid.NewGuid()}")).StatusCode);
    }

    [Fact]
    public async Task An_inverted_date_range_and_out_of_range_paging_are_validation_problems()
    {
        using var configuration = new HostConfigurationScope();
        using var application = new ChannelApiFactory();
        using var client = application.CreateClient();

        var inverted = await client.PostAsJsonAsync("/api/backfill-runs/list", new
        {
            page = 1,
            pageSize = 20,
            searchText = (string?)null,
            dateFrom = "2026-10-05",
            dateTo = "2026-10-01"
        });
        var badPage = await client.PostAsJsonAsync("/api/backfill-runs/list", new
        {
            page = 0,
            pageSize = 20,
            searchText = (string?)null
        });
        var badSize = await client.PostAsJsonAsync("/api/backfill-runs/list", new
        {
            page = 1,
            pageSize = 500,
            searchText = (string?)null
        });

        Assert.Equal(HttpStatusCode.BadRequest, inverted.StatusCode);
        var problem = await inverted.Content.ReadFromJsonAsync<ValidationProblemPayload>();
        Assert.NotNull(problem);
        Assert.True(problem.Errors.ContainsKey("dateTo"));

        Assert.Equal(HttpStatusCode.BadRequest, badPage.StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, badSize.StatusCode);
    }

    [Fact]
    public async Task An_invalid_run_id_is_a_validation_problem()
    {
        using var configuration = new HostConfigurationScope();
        using var application = new ChannelApiFactory();
        using var client = application.CreateClient();

        var response = await client.GetAsync("/api/backfill-runs/not-a-guid");

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var problem = await response.Content.ReadFromJsonAsync<ValidationProblemPayload>();
        Assert.NotNull(problem);
        Assert.True(problem.Errors.ContainsKey("runId"));
    }

    private static object ListBody() => new { page = 1, pageSize = 20, searchText = (string?)null };

    private static async Task<RunPagePayload> ReadPageAsync(HttpClient client, object body)
    {
        var response = await client.PostAsJsonAsync("/api/backfill-runs/list", body);
        response.EnsureSuccessStatusCode();
        var page = await response.Content.ReadFromJsonAsync<RunPagePayload>();
        Assert.NotNull(page);
        return page;
    }

    private static Channel ApprovedChannel(string name, string youtubeChannelId) => new()
    {
        Id = Guid.NewGuid(),
        Name = name,
        YoutubeChannelId = youtubeChannelId,
        ThumbnailUrl = $"https://thumb/{youtubeChannelId}",
        Status = ChannelStatus.APPROVED,
        ApprovedAt = Now.AddDays(-5),
        BackfillStatus = BackfillStatus.COMPLETED,
        DiscoveredAt = Now.AddDays(-6),
        CreatedAt = Now.AddDays(-6),
        ModifiedAt = Now.AddDays(-5),
        IsDeleted = false
    };

    private static ChannelBackfillRun SucceededRun(Guid channelId, int pageNumber, DateTimeOffset startedAt)
    {
        var run = ChannelBackfillRun.Start(channelId, Guid.NewGuid(), BackfillTriggerSource.APPROVAL, pageNumber, startedAt);
        run.Succeed(startedAt.AddMinutes(1));
        return run;
    }

    private static ChannelBackfillRun FailedRun(
        Guid channelId,
        int pageNumber,
        DateTimeOffset startedAt,
        BackfillTriggerSource triggerSource = BackfillTriggerSource.DAILY_PUMP)
    {
        var run = ChannelBackfillRun.Start(channelId, Guid.NewGuid(), triggerSource, pageNumber, startedAt);
        run.Fail("Unable to save backfill results. Try again later.", 2, startedAt.AddMinutes(1));
        return run;
    }

    private static async Task SeedAsync(
        WebApplicationFactory<Program> factory,
        Channel channel,
        params ChannelBackfillRun[] runs)
    {
        using var scope = factory.Services.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<YtFetcherDbContext>();
        dbContext.Channels.Add(channel);
        dbContext.ChannelBackfillRuns.AddRange(runs);
        await dbContext.SaveChangesAsync();
    }

    private sealed record RunPayload(
        Guid Id,
        Guid ChannelId,
        string ChannelName,
        string YoutubeChannelId,
        string? ChannelThumbnailUrl,
        int PageNumber,
        string TriggerSource,
        string Status,
        DateTimeOffset StartedAt,
        DateTimeOffset? CompletedAt,
        string? FailureReason,
        int? FailureCountAtTime);

    private sealed record RunPagePayload(
        IReadOnlyList<RunPayload> Items,
        int Page,
        int PageSize,
        int TotalCount);

    private sealed record ValidationProblemPayload(IDictionary<string, string[]> Errors);
}
