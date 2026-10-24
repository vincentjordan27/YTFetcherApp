using Microsoft.EntityFrameworkCore;
using YtFetcher.Core.BackfillRuns;
using YtFetcher.Core.Channels;
using YtFetcher.Infrastructure.Persistence;
using YtFetcher.Infrastructure.Persistence.Repositories;

namespace YtFetcher.Api.Tests.BackfillRuns;

public class BackfillRunRepositoryTests
{
    [Fact]
    public async Task Retryable_run_ids_uses_id_as_the_tie_breaker_for_equal_start_times()
    {
        await using var dbContext = CreateDbContext();
        var channel = new Channel
        {
            Id = Guid.NewGuid(),
            YoutubeChannelId = "UC-retry-tie",
            Name = "Retry tie",
            Status = ChannelStatus.APPROVED,
            BackfillStatus = BackfillStatus.FAILED,
            IsDeleted = false
        };
        var startedAt = new DateTimeOffset(2026, 10, 6, 1, 0, 0, TimeSpan.Zero);
        var olderId = Guid.Parse("00000000-0000-0000-0000-000000000001");
        var newerId = Guid.Parse("00000000-0000-0000-0000-000000000002");
        var older = FailedRun(channel.Id, olderId, startedAt);
        var newer = FailedRun(channel.Id, newerId, startedAt);

        dbContext.Channels.Add(channel);
        dbContext.ChannelBackfillRuns.AddRange(older, newer);
        await dbContext.SaveChangesAsync();

        var retryableIds = await new BackfillRunRepository(dbContext).GetRetryableRunIdsAsync(
            [olderId, newerId],
            CancellationToken.None);

        Assert.Equal([newerId], retryableIds);
    }

    private static YtFetcherDbContext CreateDbContext() =>
        new(new DbContextOptionsBuilder<YtFetcherDbContext>()
            .UseInMemoryDatabase($"yt-fetcher-backfill-run-retry-{Guid.NewGuid():N}")
            .Options);

    private static ChannelBackfillRun FailedRun(Guid channelId, Guid id, DateTimeOffset startedAt)
    {
        var run = ChannelBackfillRun.Start(
            channelId,
            Guid.NewGuid(),
            BackfillTriggerSource.DAILY_PUMP,
            pageNumber: 1,
            startedAt);
        run.Id = id;
        run.Fail("Unable to save backfill results. Try again later.", 3, startedAt.AddMinutes(1));
        return run;
    }
}
