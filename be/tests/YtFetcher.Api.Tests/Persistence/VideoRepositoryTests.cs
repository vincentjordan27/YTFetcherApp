using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using YtFetcher.Application.Videos.DTO.Request;
using YtFetcher.Core.Channels;
using YtFetcher.Core.Videos;
using YtFetcher.Infrastructure.Persistence;
using YtFetcher.Infrastructure.Persistence.Repositories;

namespace YtFetcher.Api.Tests.Persistence;

/// <summary>
/// The video list read is one joined query over videos and their non-deleted channel: it excludes
/// soft-deleted/stopped rows, applies the case-insensitive search, orders by the requested persisted column
/// before paging, always ends on the video id, and returns the joined channel fields. The reference
/// mutations are short transactions. Runs on the InMemory provider; the PostgreSQL lock/ordering SQL is
/// proven separately.
/// </summary>
public class VideoRepositoryTests
{
    private static readonly DateTimeOffset Base = new(2026, 9, 30, 0, 0, 0, TimeSpan.Zero);

    private static readonly Guid ActorUserId = Guid.Parse("10000000-0000-0000-0000-000000000001");

    private const VideoListSortBy Views = VideoListSortBy.Views;

    private const VideoListSortDirection Desc = VideoListSortDirection.Desc;

    [Fact]
    public async Task Active_page_orders_by_views_then_id_and_returns_the_joined_channel_fields()
    {
        await using var context = CreateContext();
        var sakura = AddChannel(context, "Sakura Channel");
        var other = AddChannel(context, "Other Channel");
        await context.SaveChangesAsync();

        var lowViews = AddVideo(context, sakura, "v-low", "Mall visit", views: 100);
        var highViews = AddVideo(context, sakura, "v-high", "Festival", views: 9_000);
        var tieLowId = AddVideo(context, other, "v-tie-low", "Interview", views: 500);
        var tieHighId = AddVideo(context, other, "v-tie-high", "Concert", views: 500);
        tieLowId.Id = Guid.Parse("00000000-0000-0000-0000-000000000001");
        tieHighId.Id = Guid.Parse("ffffffff-ffff-ffff-ffff-ffffffffffff");
        await context.SaveChangesAsync();

        var repository = new VideoRepository(context);

        var rows = await repository.GetActivePageAsync(1, 10, null, Views, Desc, CancellationToken.None);

        Assert.Equal(
            [highViews.Id, tieHighId.Id, tieLowId.Id, lowViews.Id],
            rows.Select(row => row.Video.Id));

        // The provider channel id travels with the same joined query as the display name.
        Assert.Equal("Sakura Channel", rows[0].ChannelName);
        Assert.Equal(sakura.YoutubeChannelId, rows[0].YoutubeChannelId);
        Assert.Equal(other.YoutubeChannelId, rows[1].YoutubeChannelId);
    }

    [Fact]
    public async Task Active_page_excludes_soft_deleted_videos_soft_deleted_channels_and_stopped_videos()
    {
        await using var context = CreateContext();
        var kept = AddChannel(context, "Sakura Channel");
        var deletedChannel = AddChannel(context, "Deleted Channel");
        deletedChannel.IsDeleted = true;
        await context.SaveChangesAsync();

        var keptVideo = AddVideo(context, kept, "v-kept", "Kept video");
        var deletedVideo = AddVideo(context, kept, "v-deleted", "Deleted video");
        deletedVideo.IsDeleted = true;
        var stoppedVideo = AddVideo(context, kept, "v-stopped", "Stopped video");
        stoppedVideo.TrackingStatus = VideoTrackingStatus.STOPPED;
        AddVideo(context, deletedChannel, "v-orphaned", "Orphaned video");
        await context.SaveChangesAsync();

        var repository = new VideoRepository(context);

        var rows = await repository.GetActivePageAsync(1, 10, null, Views, Desc, CancellationToken.None);
        var totalCount = await repository.CountActiveAsync(null, CancellationToken.None);

        Assert.Equal(keptVideo.Id, Assert.Single(rows).Video.Id);
        Assert.Equal(1, totalCount);
    }

    [Fact]
    public async Task Active_page_search_matches_the_video_title_or_the_channel_name_case_insensitively()
    {
        await using var context = CreateContext();
        var sakura = AddChannel(context, "Sakura Channel");
        var other = AddChannel(context, "Kyoto Walks");
        await context.SaveChangesAsync();

        var byTitle = AddVideo(context, other, "v-title", "Sakura festival");
        var byChannel = AddVideo(context, sakura, "v-channel", "Morning walk");
        AddVideo(context, other, "v-other", "Unrelated");
        await context.SaveChangesAsync();

        var repository = new VideoRepository(context);

        var rows = await repository.GetActivePageAsync(1, 10, "sAkUrA", Views, Desc, CancellationToken.None);
        var totalCount = await repository.CountActiveAsync("sAkUrA", CancellationToken.None);

        Assert.Equal(
            new[] { byTitle.Id, byChannel.Id }.Order(),
            rows.Select(row => row.Video.Id).Order());
        Assert.Equal(2, totalCount);
    }

    [Fact]
    public async Task Active_page_applies_the_page_window_after_ordering_and_returns_the_matching_total_count()
    {
        await using var context = CreateContext();
        var channel = AddChannel(context, "Sakura Channel");
        await context.SaveChangesAsync();

        for (var index = 0; index < 5; index++)
        {
            AddVideo(context, channel, $"v-{index}", $"Video {index}", views: index * 100);
        }

        await context.SaveChangesAsync();
        var repository = new VideoRepository(context);

        var rows = await repository.GetActivePageAsync(2, 2, null, Views, Desc, CancellationToken.None);
        var totalCount = await repository.CountActiveAsync(null, CancellationToken.None);

        Assert.Equal(new[] { "v-2", "v-1" }, rows.Select(row => row.Video.YoutubeVideoId));
        Assert.Equal(5, totalCount);
    }

    [Fact]
    public async Task Reference_page_includes_only_reference_videos_and_preserves_rejected_channel_videos()
    {
        await using var context = CreateContext();
        var approved = AddChannel(context, "Sakura Channel");
        var rejected = AddChannel(context, "Rejected Channel");
        rejected.Status = ChannelStatus.REJECTED;
        await context.SaveChangesAsync();

        var reference = AddVideo(context, approved, "v-reference", "Saved clip", views: 400);
        reference.IsReference = true;
        reference.TrackingStatus = VideoTrackingStatus.STOPPED;

        // A rejected channel's video is still eligible; channel lifecycle is never a video filter.
        var rejectedVideo = AddVideo(context, rejected, "v-rejected", "Rejected channel clip", views: 900);
        var active = AddVideo(context, approved, "v-active", "Active clip", views: 700);
        var deleted = AddVideo(context, approved, "v-deleted", "Deleted clip", views: 800);
        deleted.IsDeleted = true;
        await context.SaveChangesAsync();

        var repository = new VideoRepository(context);

        var referenceRows = await repository.GetReferencePageAsync(1, 10, null, Views, Desc, CancellationToken.None);
        var activeRows = await repository.GetActivePageAsync(1, 10, null, Views, Desc, CancellationToken.None);

        var referenceRow = Assert.Single(referenceRows);
        Assert.Equal(reference.Id, referenceRow.Video.Id);
        Assert.Equal("Sakura Channel", referenceRow.ChannelName);
        Assert.Equal(approved.YoutubeChannelId, referenceRow.YoutubeChannelId);
        Assert.Equal(new[] { rejectedVideo.Id, active.Id }, activeRows.Select(row => row.Video.Id));
        Assert.Equal(1, await repository.CountReferenceAsync(null, CancellationToken.None));
        Assert.Equal(2, await repository.CountActiveAsync(null, CancellationToken.None));
    }

    /// <summary>
    /// The same four rows are ordered by every permitted column in both directions. Views and likes carry a
    /// tie, and the title pair differs only by case, so the deterministic id tie-breaker (which mirrors the
    /// requested direction) is exercised.
    /// </summary>
    [Theory]
    [InlineData(VideoListSortBy.Views, VideoListSortDirection.Asc, new[] { 2, 4, 3, 1 })]
    [InlineData(VideoListSortBy.Views, VideoListSortDirection.Desc, new[] { 1, 3, 4, 2 })]
    [InlineData(VideoListSortBy.Likes, VideoListSortDirection.Asc, new[] { 3, 1, 4, 2 })]
    [InlineData(VideoListSortBy.Likes, VideoListSortDirection.Desc, new[] { 2, 4, 1, 3 })]
    [InlineData(VideoListSortBy.PublishedAt, VideoListSortDirection.Asc, new[] { 4, 1, 3, 2 })]
    [InlineData(VideoListSortBy.PublishedAt, VideoListSortDirection.Desc, new[] { 2, 3, 1, 4 })]
    [InlineData(VideoListSortBy.Title, VideoListSortDirection.Asc, new[] { 2, 3, 1, 4 })]
    [InlineData(VideoListSortBy.Title, VideoListSortDirection.Desc, new[] { 4, 1, 3, 2 })]
    [InlineData(VideoListSortBy.LastUpdatedAt, VideoListSortDirection.Asc, new[] { 1, 4, 3, 2 })]
    [InlineData(VideoListSortBy.LastUpdatedAt, VideoListSortDirection.Desc, new[] { 3, 4, 1, 2 })]
    public async Task Every_sort_field_and_direction_produces_the_global_order(
        VideoListSortBy sortBy,
        VideoListSortDirection sortDirection,
        int[] expectedOrder)
    {
        await using var context = CreateContext();
        var ids = await SeedOrderingDatasetAsync(context);
        var repository = new VideoRepository(context);

        var rows = await repository.GetActivePageAsync(1, 10, null, sortBy, sortDirection, CancellationToken.None);

        Assert.Equal(
            expectedOrder.Select(position => ids[position - 1]),
            rows.Select(row => row.Video.Id));
    }

    [Fact]
    public async Task Last_updated_sort_keeps_the_null_timestamp_last_in_both_directions()
    {
        await using var context = CreateContext();
        var ids = await SeedOrderingDatasetAsync(context);
        var repository = new VideoRepository(context);

        var ascending = await repository.GetActivePageAsync(
            1, 10, null, VideoListSortBy.LastUpdatedAt, VideoListSortDirection.Asc, CancellationToken.None);
        var descending = await repository.GetActivePageAsync(
            1, 10, null, VideoListSortBy.LastUpdatedAt, VideoListSortDirection.Desc, CancellationToken.None);

        // Row 2 is the only row without a fetch timestamp.
        Assert.Equal(ids[1], ascending[^1].Video.Id);
        Assert.Equal(ids[1], descending[^1].Video.Id);
    }

    [Fact]
    public async Task Ordering_is_applied_before_the_page_window()
    {
        await using var context = CreateContext();
        var ids = await SeedOrderingDatasetAsync(context);
        var repository = new VideoRepository(context);

        var pageOne = await repository.GetActivePageAsync(
            1, 2, null, VideoListSortBy.Views, VideoListSortDirection.Desc, CancellationToken.None);
        var pageTwo = await repository.GetActivePageAsync(
            2, 2, null, VideoListSortBy.Views, VideoListSortDirection.Desc, CancellationToken.None);

        Assert.Equal(new[] { ids[0], ids[2] }, pageOne.Select(row => row.Video.Id));
        // The 100-view tie is broken by the direction-mirrored id key, so page two starts with the higher id.
        Assert.Equal(new[] { ids[3], ids[1] }, pageTwo.Select(row => row.Video.Id));
    }

    [Fact]
    public async Task Remove_reference_returns_a_young_video_to_active_and_leaves_its_statistics_untouched()
    {
        await using var context = CreateContext();
        var channel = AddChannel(context, "Sakura Channel");
        await context.SaveChangesAsync();
        var video = AddVideo(context, channel, "v-young", "Young clip", views: 1_200);
        video.IsReference = true;
        video.ReferenceSetAt = Base.AddDays(-2);
        video.TrackingStatus = VideoTrackingStatus.STOPPED;
        await context.SaveChangesAsync();

        var repository = new VideoRepository(context);

        var removed = await repository.TryRemoveReferenceAsync(video.Id, ActorUserId, Base, CancellationToken.None);

        Assert.True(removed);

        var stored = await context.Videos.AsNoTracking().SingleAsync(entity => entity.Id == video.Id);
        Assert.False(stored.IsReference);
        Assert.Equal(VideoTrackingStatus.ACTIVE, stored.TrackingStatus);
        Assert.Equal(Base, stored.ModifiedAt);

        // The reference metadata is cleared so a non-reference row never claims a reference timestamp.
        Assert.Null(stored.ReferenceSetAt);
        Assert.Null(stored.ReferenceSetByUserId);

        // Counts, duration, publication time, availability, and the fetch time are untouched.
        Assert.Equal(1_200, stored.CurrentViews);
        Assert.Equal(10, stored.CurrentLikes);
        Assert.Equal(120_000, stored.DurationMillis);
        Assert.Equal(Base.AddDays(-10), stored.PublishedAt);
        Assert.Equal(Base, stored.LastStatsFetchedAt);
    }

    [Fact]
    public async Task Remove_reference_keeps_a_mature_and_popular_video_stopped()
    {
        await using var context = CreateContext();
        var channel = AddChannel(context, "Sakura Channel");
        await context.SaveChangesAsync();
        var video = AddVideo(context, channel, "v-mature", "Mature clip", views: 6_000_000);
        video.PublishedAt = Base.AddDays(-(Video.MatureAgeDays + 1));
        video.IsReference = true;
        video.TrackingStatus = VideoTrackingStatus.STOPPED;
        await context.SaveChangesAsync();

        var repository = new VideoRepository(context);

        Assert.True(await repository.TryRemoveReferenceAsync(video.Id, ActorUserId, Base, CancellationToken.None));

        var stored = await context.Videos.AsNoTracking().SingleAsync(entity => entity.Id == video.Id);
        Assert.False(stored.IsReference);
        Assert.Equal(VideoTrackingStatus.STOPPED, stored.TrackingStatus);
    }

    [Fact]
    public async Task Remove_reference_rejects_a_row_that_is_missing_deleted_or_not_a_reference()
    {
        await using var context = CreateContext();
        var channel = AddChannel(context, "Sakura Channel");
        await context.SaveChangesAsync();
        var active = AddVideo(context, channel, "v-active", "Active clip");
        var deleted = AddVideo(context, channel, "v-deleted", "Deleted clip");
        deleted.IsReference = true;
        deleted.IsDeleted = true;
        await context.SaveChangesAsync();

        var repository = new VideoRepository(context);

        Assert.False(await repository.TryRemoveReferenceAsync(Guid.NewGuid(), ActorUserId, Base, CancellationToken.None));
        Assert.False(await repository.TryRemoveReferenceAsync(deleted.Id, ActorUserId, Base, CancellationToken.None));
        Assert.False(await repository.TryRemoveReferenceAsync(active.Id, ActorUserId, Base, CancellationToken.None));

        // The non-reference row keeps its own state and is never mutated into a reference.
        var stored = await context.Videos.AsNoTracking().SingleAsync(entity => entity.Id == active.Id);
        Assert.False(stored.IsReference);
        Assert.Equal(VideoTrackingStatus.ACTIVE, stored.TrackingStatus);
        Assert.Equal(Base, stored.ModifiedAt);
    }

    [Fact]
    public async Task Remove_reference_touches_only_the_requested_row()
    {
        await using var context = CreateContext();
        var channel = AddChannel(context, "Sakura Channel");
        await context.SaveChangesAsync();
        var target = AddVideo(context, channel, "v-target", "Target clip");
        var other = AddVideo(context, channel, "v-other", "Other clip");
        foreach (var video in new[] { target, other })
        {
            video.IsReference = true;
            video.TrackingStatus = VideoTrackingStatus.STOPPED;
        }

        await context.SaveChangesAsync();

        var repository = new VideoRepository(context);

        Assert.True(await repository.TryRemoveReferenceAsync(target.Id, ActorUserId, Base, CancellationToken.None));

        var stored = await context.Videos.AsNoTracking().SingleAsync(entity => entity.Id == other.Id);
        Assert.True(stored.IsReference);
        Assert.Equal(VideoTrackingStatus.STOPPED, stored.TrackingStatus);
    }

    [Fact]
    public async Task Mark_reference_commits_every_selected_active_video_in_one_batch()
    {
        await using var context = CreateContext();
        var channel = AddChannel(context, "Sakura Channel");
        await context.SaveChangesAsync();
        var first = AddVideo(context, channel, "v-1", "First", views: 100);
        var second = AddVideo(context, channel, "v-2", "Second", views: 200);
        await context.SaveChangesAsync();

        var repository = new VideoRepository(context);

        var committed = await repository.TryMarkAsReferenceAsync(
            [first.Id, second.Id],
            ActorUserId,
            Base,
            CancellationToken.None);

        Assert.True(committed);

        var stored = await context.Videos
            .AsNoTracking()
            .Where(video => video.Id == first.Id || video.Id == second.Id)
            .ToListAsync();

        Assert.Equal(2, stored.Count);
        Assert.All(stored, video =>
        {
            Assert.True(video.IsReference);
            Assert.Equal(VideoTrackingStatus.STOPPED, video.TrackingStatus);
            Assert.Equal(Base, video.ReferenceSetAt);
            Assert.Equal(ActorUserId, video.ReferenceSetByUserId);
            Assert.Equal(ActorUserId, video.ModifiedByUserId);
            Assert.Equal(120_000, video.DurationMillis);
            Assert.Equal(10, video.CurrentLikes);
        });
    }

    [Fact]
    public async Task Mark_reference_rolls_back_the_whole_batch_when_one_video_is_not_active()
    {
        await using var context = CreateContext();
        var channel = AddChannel(context, "Sakura Channel");
        await context.SaveChangesAsync();
        var active = AddVideo(context, channel, "v-active", "Active", views: 100);
        var stopped = AddVideo(context, channel, "v-stopped", "Stopped", views: 200);
        stopped.TrackingStatus = VideoTrackingStatus.STOPPED;
        await context.SaveChangesAsync();

        var repository = new VideoRepository(context);

        var committed = await repository.TryMarkAsReferenceAsync(
            [active.Id, stopped.Id],
            ActorUserId,
            Base,
            CancellationToken.None);

        Assert.False(committed);

        var reloaded = await context.Videos.AsNoTracking().SingleAsync(video => video.Id == active.Id);
        Assert.False(reloaded.IsReference);
        Assert.Equal(VideoTrackingStatus.ACTIVE, reloaded.TrackingStatus);
    }

    [Fact]
    public async Task Snapshot_history_is_newest_first_paged_and_excludes_soft_deleted_rows()
    {
        await using var context = CreateContext();
        var channel = AddChannel(context, "Sakura Channel");
        await context.SaveChangesAsync();
        var video = AddVideo(context, channel, "v-history", "History video");
        await context.SaveChangesAsync();

        var newest = AddSnapshot(context, video, 30, Base.AddHours(-1), views: 3_000);
        var middle = AddSnapshot(context, video, 20, Base.AddHours(-2), views: 2_000);
        AddSnapshot(context, video, 10, Base.AddHours(-3), views: 1_000);
        var deleted = AddSnapshot(context, video, 40, Base, views: 4_000);
        deleted.IsDeleted = true;
        await context.SaveChangesAsync();

        var repository = new VideoRepository(context);

        var (exists, totalCount, pageOne) = await repository.GetSnapshotHistoryPageAsync(
            video.Id,
            1,
            1,
            CancellationToken.None);
        var (_, _, pageTwo) = await repository.GetSnapshotHistoryPageAsync(
            video.Id,
            2,
            1,
            CancellationToken.None);

        Assert.True(exists);
        Assert.Equal(3, totalCount);
        Assert.Equal(newest.Id, Assert.Single(pageOne).Id);
        Assert.Equal(middle.Id, Assert.Single(pageTwo).Id);
    }

    [Fact]
    public async Task Snapshot_history_reports_a_missing_or_soft_deleted_video_as_absent()
    {
        await using var context = CreateContext();
        var channel = AddChannel(context, "Sakura Channel");
        await context.SaveChangesAsync();
        var deleted = AddVideo(context, channel, "v-deleted", "Deleted");
        deleted.IsDeleted = true;
        await context.SaveChangesAsync();

        var repository = new VideoRepository(context);

        var missing = await repository.GetSnapshotHistoryPageAsync(Guid.NewGuid(), 1, 10, CancellationToken.None);
        var softDeleted = await repository.GetSnapshotHistoryPageAsync(deleted.Id, 1, 10, CancellationToken.None);

        Assert.False(missing.Exists);
        Assert.False(softDeleted.Exists);
        Assert.Empty(missing.Snapshots);
    }

    /// <summary>
    /// Four active videos built so that every sortable column has a meaningful order, one tie, and one
    /// missing timestamp. Returns the video ids in seed order.
    /// </summary>
    private static async Task<Guid[]> SeedOrderingDatasetAsync(YtFetcherDbContext context)
    {
        // Rows 2 and 3 share a lowercased title so the deterministic id tie-breaker is what separates them.
        var zeta = AddChannel(context, "Zeta Channel");
        var beta = AddChannel(context, "Beta Channel");
        var alpha = AddChannel(context, "Alpha Channel");
        var gamma = AddChannel(context, "Gamma Channel");
        await context.SaveChangesAsync();

        var first = AddVideo(context, zeta, "v-1", "beta clip", views: 300, likes: 30);
        var second = AddVideo(context, beta, "v-2", "Alpha clip", views: 100, likes: 50);
        var third = AddVideo(context, alpha, "v-3", "alpha clip", views: 200, likes: 10);
        var fourth = AddVideo(context, gamma, "v-4", "gamma clip", views: 100, likes: 30);

        first.Id = Guid.Parse("00000000-0000-0000-0000-000000000001");
        second.Id = Guid.Parse("00000000-0000-0000-0000-000000000002");
        third.Id = Guid.Parse("00000000-0000-0000-0000-000000000003");
        fourth.Id = Guid.Parse("00000000-0000-0000-0000-000000000004");

        first.PublishedAt = Base.AddDays(-30);
        second.PublishedAt = Base.AddDays(-10);
        third.PublishedAt = Base.AddDays(-20);
        fourth.PublishedAt = Base.AddDays(-40);

        first.LastStatsFetchedAt = Base.AddDays(-3);
        second.LastStatsFetchedAt = null;
        third.LastStatsFetchedAt = Base.AddDays(-1);
        fourth.LastStatsFetchedAt = Base.AddDays(-2);

        await context.SaveChangesAsync();

        return [first.Id, second.Id, third.Id, fourth.Id];
    }

    private static VideoSnapshot AddSnapshot(
        YtFetcherDbContext context,
        Video video,
        long id,
        DateTimeOffset capturedAt,
        long views)
    {
        var snapshot = new VideoSnapshot
        {
            Id = id,
            VideoId = video.Id,
            CapturedAt = capturedAt,
            ViewCount = views,
            LikeCount = views / 10,
            IsDeleted = false,
            CreatedAt = capturedAt,
            ModifiedAt = capturedAt
        };

        context.VideoSnapshots.Add(snapshot);
        return snapshot;
    }

    private static YtFetcherDbContext CreateContext() =>
        new(new DbContextOptionsBuilder<YtFetcherDbContext>()
            .UseInMemoryDatabase($"yt-fetcher-videos-{Guid.NewGuid():N}")
            // The reference mutations own a PostgreSQL transaction. The in-memory provider has no
            // transactions, so its warning is ignored here; the real lock/rollback behavior is
            // PostgreSQL-only.
            .ConfigureWarnings(warnings => warnings.Ignore(InMemoryEventId.TransactionIgnoredWarning))
            .Options);

    private static Channel AddChannel(YtFetcherDbContext context, string name)
    {
        var channel = new Channel
        {
            Id = Guid.NewGuid(),
            YoutubeChannelId = $"UC-{Guid.NewGuid():N}",
            Name = name,
            Status = ChannelStatus.APPROVED,
            ApprovedAt = Base,
            BackfillStatus = BackfillStatus.COMPLETED,
            IsDeleted = false,
            DiscoveredAt = Base,
            CreatedAt = Base,
            ModifiedAt = Base
        };

        context.Channels.Add(channel);
        return channel;
    }

    private static Video AddVideo(
        YtFetcherDbContext context,
        Channel channel,
        string youtubeVideoId,
        string title,
        long views = 100,
        long likes = 10)
    {
        var video = new Video
        {
            Id = Guid.NewGuid(),
            YoutubeVideoId = youtubeVideoId,
            ChannelId = channel.Id,
            Title = title,
            ThumbnailUrl = $"https://example.com/{youtubeVideoId}.jpg",
            PublishedAt = Base.AddDays(-10),
            DurationMillis = 120_000,
            CurrentViews = views,
            CurrentLikes = likes,
            TrackingStatus = VideoTrackingStatus.ACTIVE,
            LastStatsFetchedAt = Base,
            IsDeleted = false,
            CreatedAt = Base,
            ModifiedAt = Base
        };

        context.Videos.Add(video);
        return video;
    }
}
