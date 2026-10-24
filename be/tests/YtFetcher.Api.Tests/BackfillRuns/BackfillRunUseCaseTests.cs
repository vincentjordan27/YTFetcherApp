using YtFetcher.Application.BackfillRuns.DTO.Request;
using YtFetcher.Application.BackfillRuns.UseCases;
using YtFetcher.Core.BackfillRuns;
using YtFetcher.Core.Channels;

namespace YtFetcher.Api.Tests.BackfillRuns;

/// <summary>
/// The backfill run history reads: joined channel data without a query per row, the optional filters,
/// the inclusive UTC date window, and the exclusion of soft-deleted runs.
/// </summary>
public class BackfillRunUseCaseTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 5, 4, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task The_page_returns_joined_channel_data_newest_first_with_the_total_count()
    {
        var repository = new InMemoryBackfillRunRepository();
        var channel = SeedChannel(repository, "Sakura Clips", "UC-sakura");
        SeedSucceededRun(repository, channel, pageNumber: 1, startedAt: Now.AddDays(-3));
        var newest = SeedFailedRun(repository, channel, pageNumber: 2, startedAt: Now.AddDays(-1));

        var response = await CreateUseCase(repository).GetPageAsync(
            new BackfillRunsListRequest(1, 1, null, null, null, null, null),
            CancellationToken.None);

        var item = Assert.Single(response.Items);
        Assert.Equal(newest.Id, item.Id);
        Assert.Equal(channel.Id, item.ChannelId);
        Assert.Equal("Sakura Clips", item.ChannelName);
        Assert.Equal("UC-sakura", item.YoutubeChannelId);
        Assert.Equal("https://thumb/UC-sakura", item.ChannelThumbnailUrl);
        Assert.Equal(2, item.PageNumber);
        Assert.Equal(BackfillTriggerSource.DAILY_PUMP, item.TriggerSource);
        Assert.Equal(BackfillRunStatus.FAILED, item.Status);
        Assert.Equal("Unable to save backfill results. Try again later.", item.FailureReason);
        Assert.Equal(2, item.FailureCountAtTime);
        Assert.NotNull(item.CompletedAt);
        Assert.Equal(1, response.Page);
        Assert.Equal(1, response.PageSize);
        Assert.Equal(2, response.TotalCount);
    }

    [Fact]
    public async Task Search_matches_the_channel_name_case_insensitively()
    {
        var repository = new InMemoryBackfillRunRepository();
        var matching = SeedChannel(repository, "Vtuber Weekly", "UC-vtuber");
        var other = SeedChannel(repository, "Sakura Clips", "UC-sakura");
        SeedSucceededRun(repository, matching, 1, Now.AddDays(-2));
        SeedSucceededRun(repository, other, 1, Now.AddDays(-1));

        var response = await CreateUseCase(repository).GetPageAsync(
            new BackfillRunsListRequest(1, 20, "  VTUBER  ", null, null, null, null),
            CancellationToken.None);

        var item = Assert.Single(response.Items);
        Assert.Equal("Vtuber Weekly", item.ChannelName);
        Assert.Equal(1, response.TotalCount);
    }

    [Fact]
    public async Task Search_matches_the_youtube_channel_id()
    {
        var repository = new InMemoryBackfillRunRepository();
        var matching = SeedChannel(repository, "Vtuber Weekly", "UC-abc123");
        var other = SeedChannel(repository, "Sakura Clips", "UC-sakura");
        SeedSucceededRun(repository, matching, 1, Now.AddDays(-2));
        SeedSucceededRun(repository, other, 1, Now.AddDays(-1));

        var response = await CreateUseCase(repository).GetPageAsync(
            new BackfillRunsListRequest(1, 20, "ABC123", null, null, null, null),
            CancellationToken.None);

        var item = Assert.Single(response.Items);
        Assert.Equal("UC-abc123", item.YoutubeChannelId);
    }

    [Fact]
    public async Task The_status_and_trigger_filters_match_exactly()
    {
        var repository = new InMemoryBackfillRunRepository();
        var channel = SeedChannel(repository, "Sakura Clips", "UC-sakura");
        SeedSucceededRun(repository, channel, 1, Now.AddDays(-3));
        SeedFailedRun(repository, channel, 2, Now.AddDays(-2), BackfillTriggerSource.APPROVAL);
        SeedFailedRun(repository, channel, 3, Now.AddDays(-1), BackfillTriggerSource.RECOVERY);

        var byStatus = await CreateUseCase(repository).GetPageAsync(
            new BackfillRunsListRequest(1, 20, null, BackfillRunStatus.FAILED, null, null, null),
            CancellationToken.None);
        Assert.Equal(2, byStatus.TotalCount);
        Assert.All(byStatus.Items, item => Assert.Equal(BackfillRunStatus.FAILED, item.Status));

        var byTrigger = await CreateUseCase(repository).GetPageAsync(
            new BackfillRunsListRequest(1, 20, null, null, BackfillTriggerSource.RECOVERY, null, null),
            CancellationToken.None);
        var triggerItem = Assert.Single(byTrigger.Items);
        Assert.Equal(BackfillRunStatus.FAILED, triggerItem.Status);
        Assert.Equal(BackfillTriggerSource.RECOVERY, triggerItem.TriggerSource);
        Assert.Equal(3, triggerItem.PageNumber);
    }

    [Fact]
    public async Task The_date_filters_cover_whole_utc_days_inclusively()
    {
        var repository = new InMemoryBackfillRunRepository();
        var channel = SeedChannel(repository, "Sakura Clips", "UC-sakura");
        SeedSucceededRun(repository, channel, 1, new DateTimeOffset(2026, 9, 30, 23, 59, 59, TimeSpan.Zero));
        SeedSucceededRun(repository, channel, 2, new DateTimeOffset(2026, 10, 1, 0, 0, 0, TimeSpan.Zero));
        SeedSucceededRun(repository, channel, 3, new DateTimeOffset(2026, 10, 2, 23, 59, 59, TimeSpan.Zero));
        SeedSucceededRun(repository, channel, 4, new DateTimeOffset(2026, 10, 3, 0, 0, 0, TimeSpan.Zero));

        var response = await CreateUseCase(repository).GetPageAsync(
            new BackfillRunsListRequest(
                1,
                20,
                null,
                null,
                null,
                new DateOnly(2026, 10, 1),
                new DateOnly(2026, 10, 2)),
            CancellationToken.None);

        Assert.Equal(2, response.TotalCount);
        Assert.Equal(
            [new DateTimeOffset(2026, 10, 2, 23, 59, 59, TimeSpan.Zero), new DateTimeOffset(2026, 10, 1, 0, 0, 0, TimeSpan.Zero)],
            response.Items.Select(item => item.StartedAt).ToArray());
    }

    [Fact]
    public async Task A_soft_deleted_run_is_never_listed_or_returned()
    {
        var repository = new InMemoryBackfillRunRepository();
        var channel = SeedChannel(repository, "Sakura Clips", "UC-sakura");
        var visible = SeedSucceededRun(repository, channel, 1, Now.AddDays(-2));
        var deleted = SeedSucceededRun(repository, channel, 2, Now.AddDays(-1));
        repository.SeedRun(deleted);
        deleted.IsDeleted = true;

        var useCase = CreateUseCase(repository);

        var page = await useCase.GetPageAsync(
            new BackfillRunsListRequest(1, 20, null, null, null, null, null),
            CancellationToken.None);
        Assert.Equal(1, page.TotalCount);
        Assert.Equal(visible.Id, page.Items[0].Id);

        Assert.Null(await useCase.GetByIdAsync(deleted.Id, CancellationToken.None));
        Assert.NotNull(await useCase.GetByIdAsync(visible.Id, CancellationToken.None));
    }

    [Fact]
    public async Task The_detail_returns_the_joined_channel_and_the_persisted_failure_state()
    {
        var repository = new InMemoryBackfillRunRepository();
        var channel = SeedChannel(repository, "Sakura Clips", "UC-sakura");
        var run = SeedFailedRun(repository, channel, 4, Now.AddDays(-1));

        var detail = await CreateUseCase(repository).GetByIdAsync(run.Id, CancellationToken.None);

        Assert.NotNull(detail);
        Assert.Equal(run.Id, detail.Id);
        Assert.Equal("Sakura Clips", detail.ChannelName);
        Assert.Equal(4, detail.PageNumber);
        Assert.Equal("Unable to save backfill results. Try again later.", detail.FailureReason);
        Assert.Equal(2, detail.FailureCountAtTime);
    }

    [Fact]
    public async Task An_unknown_run_id_has_no_detail()
    {
        var repository = new InMemoryBackfillRunRepository();

        Assert.Null(await CreateUseCase(repository).GetByIdAsync(Guid.NewGuid(), CancellationToken.None));
    }

    [Fact]
    public async Task An_empty_history_page_reports_zero_everywhere()
    {
        var repository = new InMemoryBackfillRunRepository();

        var response = await CreateUseCase(repository).GetPageAsync(
            new BackfillRunsListRequest(1, 20, null, null, null, null, null),
            CancellationToken.None);

        Assert.Empty(response.Items);
        Assert.Equal(0, response.TotalCount);
    }

    [Fact]
    public async Task Can_retry_is_true_only_for_the_newest_failed_run_of_a_currently_failed_channel()
    {
        var repository = new InMemoryBackfillRunRepository();
        var channel = SeedChannel(repository, "Sakura Clips", "UC-sakura", BackfillStatus.FAILED);
        var newestFailure = SeedFailedRun(repository, channel, 3, Now.AddDays(-1));
        var olderFailure = SeedFailedRun(repository, channel, 2, Now.AddDays(-3));

        var page = await CreateUseCase(repository).GetPageAsync(
            new BackfillRunsListRequest(1, 20, null, null, null, null, null),
            CancellationToken.None);

        Assert.True(page.Items.Single(item => item.Id == newestFailure.Id).CanRetry);

        // An older failure is not the channel's newest run, so no retry is offered for it.
        Assert.False(page.Items.Single(item => item.Id == olderFailure.Id).CanRetry);
    }

    [Theory]
    [InlineData("SUCCEEDED")]
    [InlineData("RUNNING")]
    public async Task Can_retry_is_false_when_a_newer_run_of_any_status_exists(string newerStatus)
    {
        var repository = new InMemoryBackfillRunRepository();
        var channel = SeedChannel(repository, "Sakura Clips", "UC-sakura", BackfillStatus.FAILED);
        var failed = SeedFailedRun(repository, channel, 2, Now.AddDays(-2));

        if (newerStatus == "SUCCEEDED")
        {
            SeedSucceededRun(repository, channel, 3, Now.AddDays(-1));
        }
        else
        {
            repository.SeedRun(ChannelBackfillRun.Start(
                channel.Id, Guid.NewGuid(), BackfillTriggerSource.DAILY_PUMP, 3, Now.AddDays(-1)));
        }

        var page = await CreateUseCase(repository).GetPageAsync(
            new BackfillRunsListRequest(1, 20, null, null, null, null, null),
            CancellationToken.None);

        Assert.False(page.Items.Single(item => item.Id == failed.Id).CanRetry);
    }

    [Fact]
    public async Task Can_retry_is_false_when_the_channel_is_not_a_currently_failed_approved_channel()
    {
        var repository = new InMemoryBackfillRunRepository();
        var pending = SeedChannel(repository, "Pending Clips", "UC-pending", BackfillStatus.PENDING);
        var rejected = SeedChannel(repository, "Rejected Clips", "UC-rejected", BackfillStatus.FAILED,
            ChannelStatus.REJECTED);
        var deleted = SeedChannel(repository, "Deleted Clips", "UC-deleted", BackfillStatus.FAILED,
            isDeleted: true);
        var pendingRun = SeedFailedRun(repository, pending, 3, Now.AddDays(-1));
        var rejectedRun = SeedFailedRun(repository, rejected, 3, Now.AddDays(-2));
        var deletedRun = SeedFailedRun(repository, deleted, 3, Now.AddDays(-3));

        var page = await CreateUseCase(repository).GetPageAsync(
            new BackfillRunsListRequest(1, 20, null, null, null, null, null),
            CancellationToken.None);

        Assert.False(page.Items.Single(item => item.Id == pendingRun.Id).CanRetry);
        Assert.False(page.Items.Single(item => item.Id == rejectedRun.Id).CanRetry);
        Assert.False(page.Items.Single(item => item.Id == deletedRun.Id).CanRetry);
    }

    [Fact]
    public async Task The_detail_carries_the_same_server_decision_as_the_list()
    {
        var repository = new InMemoryBackfillRunRepository();
        var channel = SeedChannel(repository, "Sakura Clips", "UC-sakura", BackfillStatus.FAILED);
        var run = SeedFailedRun(repository, channel, 3, Now.AddDays(-1));

        var detail = await CreateUseCase(repository).GetByIdAsync(run.Id, CancellationToken.None);

        Assert.NotNull(detail);
        Assert.True(detail.CanRetry);
    }

    private static BackfillRunUseCase CreateUseCase(InMemoryBackfillRunRepository repository) => new(repository);

    private static Channel SeedChannel(
        InMemoryBackfillRunRepository repository,
        string name,
        string youtubeChannelId,
        BackfillStatus backfillStatus = BackfillStatus.PENDING,
        ChannelStatus status = ChannelStatus.APPROVED,
        bool isDeleted = false)
    {
        var channel = new Channel
        {
            Id = Guid.NewGuid(),
            Name = name,
            YoutubeChannelId = youtubeChannelId,
            ThumbnailUrl = $"https://thumb/{youtubeChannelId}",
            Status = status,
            BackfillStatus = backfillStatus,
            IsDeleted = isDeleted,
            CreatedAt = Now.AddDays(-10),
            ModifiedAt = Now.AddDays(-10),
            DiscoveredAt = Now.AddDays(-10)
        };

        repository.SeedChannel(channel);
        return channel;
    }

    private static ChannelBackfillRun SeedSucceededRun(
        InMemoryBackfillRunRepository repository,
        Channel channel,
        int pageNumber,
        DateTimeOffset startedAt)
    {
        var run = ChannelBackfillRun.Start(
            channel.Id,
            Guid.NewGuid(),
            BackfillTriggerSource.APPROVAL,
            pageNumber,
            startedAt);

        run.Succeed(startedAt.AddMinutes(1));
        repository.SeedRun(run);
        return run;
    }

    private static ChannelBackfillRun SeedFailedRun(
        InMemoryBackfillRunRepository repository,
        Channel channel,
        int pageNumber,
        DateTimeOffset startedAt,
        BackfillTriggerSource triggerSource = BackfillTriggerSource.DAILY_PUMP)
    {
        var run = ChannelBackfillRun.Start(channel.Id, Guid.NewGuid(), triggerSource, pageNumber, startedAt);

        run.Fail("Unable to save backfill results. Try again later.", 2, startedAt.AddMinutes(1));
        repository.SeedRun(run);
        return run;
    }
}
