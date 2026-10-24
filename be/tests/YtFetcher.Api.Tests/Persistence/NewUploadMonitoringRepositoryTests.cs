using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using YtFetcher.Core.Channels;
using YtFetcher.Core.Videos;
using YtFetcher.Infrastructure.Persistence;
using YtFetcher.Infrastructure.Persistence.Repositories;

namespace YtFetcher.Api.Tests.Persistence;

/// <summary>
/// The M6 candidate read and known-id lookup: every approved, non-deleted channel is eligible,
/// and the order is a null marker first, then the oldest marker, then id. The per-channel write is a
/// PostgreSQL transaction and is proven by the PostgreSQL-only suite, not here.
/// </summary>
public class NewUploadMonitoringRepositoryTests
{
    private static readonly DateTimeOffset QuotaDateStart = new(2026, 10, 6, 7, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task Candidates_include_every_approved_channel_in_deterministic_order_regardless_of_backfill_state()
    {
        await using var context = CreateContext();

        var nullMarker = AddChannel(context, "null-marker", ChannelStatus.APPROVED, BackfillStatus.COMPLETED, null);
        var oldestMarker = AddChannel(
            context,
            "oldest-marker",
            ChannelStatus.APPROVED,
            BackfillStatus.COMPLETED,
            QuotaDateStart.AddHours(-9));
        var newerMarker = AddChannel(
            context,
            "newer-marker",
            ChannelStatus.APPROVED,
            BackfillStatus.COMPLETED,
            QuotaDateStart.AddHours(-1));

        // Excluded: wrong lifecycle, already checked today, or soft deleted.
        AddChannel(context, "pending", ChannelStatus.PENDING, BackfillStatus.COMPLETED, null);
        AddChannel(context, "rejected", ChannelStatus.REJECTED, BackfillStatus.COMPLETED, null);
        var running = AddChannel(
            context,
            "running",
            ChannelStatus.APPROVED,
            BackfillStatus.RUNNING,
            QuotaDateStart.AddHours(-2));
        AddChannel(context, "checked-today", ChannelStatus.APPROVED, BackfillStatus.COMPLETED, QuotaDateStart);
        var deleted = AddChannel(context, "deleted", ChannelStatus.APPROVED, BackfillStatus.COMPLETED, null);
        deleted.IsDeleted = true;
        await context.SaveChangesAsync();

        var repository = new NewUploadMonitoringRepository(context, TimeProvider.System);

        var candidates = await repository.GetUploadMonitorCandidatesAsync(QuotaDateStart, CancellationToken.None);

        Assert.Equal(
            [nullMarker.Id, oldestMarker.Id, running.Id, newerMarker.Id],
            candidates.Select(channel => channel.Id));
    }

    [Fact]
    public async Task Candidates_break_a_marker_tie_by_id_ascending()
    {
        await using var context = CreateContext();
        var tieMarker = QuotaDateStart.AddHours(-3);
        var highId = AddChannel(
            context,
            "tie-high",
            ChannelStatus.APPROVED,
            BackfillStatus.COMPLETED,
            tieMarker,
            id: Guid.Parse("ffffffff-ffff-ffff-ffff-ffffffffffff"));
        var lowId = AddChannel(
            context,
            "tie-low",
            ChannelStatus.APPROVED,
            BackfillStatus.COMPLETED,
            tieMarker,
            id: Guid.Parse("00000000-0000-0000-0000-000000000001"));
        await context.SaveChangesAsync();

        var repository = new NewUploadMonitoringRepository(context, TimeProvider.System);

        var candidates = await repository.GetUploadMonitorCandidatesAsync(QuotaDateStart, CancellationToken.None);

        Assert.Equal([lowId.Id, highId.Id], candidates.Select(channel => channel.Id));
    }

    [Fact]
    public async Task Known_ids_include_soft_deleted_videos_and_ignore_unknown_ids()
    {
        await using var context = CreateContext();
        var channel = AddChannel(context, "known", ChannelStatus.APPROVED, BackfillStatus.COMPLETED, null);
        await context.SaveChangesAsync();

        AddVideo(context, channel, "known-1");
        var deleted = AddVideo(context, channel, "known-deleted");
        deleted.IsDeleted = true;
        await context.SaveChangesAsync();

        var repository = new NewUploadMonitoringRepository(context, TimeProvider.System);

        var known = await repository.GetKnownYoutubeVideoIdsAsync(["known-1", "known-deleted", "missing"], CancellationToken.None);

        Assert.True(known.Contains("known-1"));
        // Soft-deleted rows are never revived and never receive a second initial snapshot.
        Assert.True(known.Contains("known-deleted"));
        Assert.False(known.Contains("missing"));
    }

    private static YtFetcherDbContext CreateContext() =>
        new(new DbContextOptionsBuilder<YtFetcherDbContext>()
            .UseInMemoryDatabase($"yt-fetcher-monitor-{Guid.NewGuid():N}")
            .ConfigureWarnings(warnings => warnings.Ignore(InMemoryEventId.TransactionIgnoredWarning))
            .Options);

    private static Channel AddChannel(
        YtFetcherDbContext context,
        string name,
        ChannelStatus status,
        BackfillStatus backfillStatus,
        DateTimeOffset? lastUploadCheckAt,
        Guid? id = null)
    {
        var channel = new Channel
        {
            Id = id ?? Guid.NewGuid(),
            YoutubeChannelId = $"UC-{Guid.NewGuid():N}",
            Name = name,
            Status = status,
            BackfillStatus = backfillStatus,
            UploadsPlaylistId = "UU-channel",
            LastUploadCheckAt = lastUploadCheckAt,
            IsDeleted = false,
            ApprovedAt = QuotaDateStart,
            DiscoveredAt = QuotaDateStart,
            CreatedAt = QuotaDateStart,
            ModifiedAt = QuotaDateStart
        };

        context.Channels.Add(channel);
        return channel;
    }

    private static Video AddVideo(YtFetcherDbContext context, Channel channel, string youtubeVideoId)
    {
        var video = new Video
        {
            Id = Guid.NewGuid(),
            YoutubeVideoId = youtubeVideoId,
            ChannelId = channel.Id,
            Title = youtubeVideoId,
            PublishedAt = QuotaDateStart.AddDays(-1),
            DurationMillis = 30_000,
            CurrentViews = 10,
            CurrentLikes = 1,
            IsDeleted = false,
            CreatedAt = QuotaDateStart,
            ModifiedAt = QuotaDateStart
        };

        context.Videos.Add(video);
        return video;
    }
}
