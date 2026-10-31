using YtFetcher.Core.Videos;

namespace YtFetcher.Api.Tests.Core;

/// <summary>
/// The one-way reference transition: it stops tracking and records reference metadata while leaving
/// content and current counts untouched.
/// </summary>
public class VideoReferenceDomainTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 1, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public void Mark_as_reference_stops_tracking_and_preserves_content_and_counts()
    {
        var video = new Video
        {
            Id = Guid.NewGuid(),
            YoutubeVideoId = "v-1",
            ChannelId = Guid.NewGuid(),
            Title = "Sakura live",
            ThumbnailUrl = "https://example.com/v-1.jpg",
            PublishedAt = Now.AddDays(-30),
            DurationMillis = 120_000,
            CurrentViews = 123,
            CurrentLikes = 45,
            TrackingStatus = VideoTrackingStatus.ACTIVE,
            LastStatsFetchedAt = Now.AddHours(-1),
            IsDeleted = false,
            CreatedAt = Now.AddDays(-30),
            ModifiedAt = Now.AddDays(-1)
        };

        var actorUserId = Guid.NewGuid();

        video.MarkAsReference(Now, actorUserId);

        Assert.True(video.IsReference);
        Assert.True(video.IsReferenceUsed);
        Assert.Equal(Now, video.ReferenceSetAt);
        Assert.Equal(actorUserId, video.ReferenceSetByUserId);
        Assert.Equal(VideoTrackingStatus.STOPPED, video.TrackingStatus);
        Assert.Equal(Now, video.ModifiedAt);

        // Content, counts, and timestamps that must not change stay intact.
        Assert.Equal("Sakura live", video.Title);
        Assert.Equal("v-1", video.YoutubeVideoId);
        Assert.Equal(120_000, video.DurationMillis);
        Assert.Equal(123, video.CurrentViews);
        Assert.Equal(45, video.CurrentLikes);
        Assert.Equal(Now.AddDays(-30), video.PublishedAt);
        Assert.Equal(Now.AddHours(-1), video.LastStatsFetchedAt);
    }

    [Fact]
    public void Mark_as_reference_records_the_actor_in_reference_and_modified_audit_fields()
    {
        var actorUserId = Guid.NewGuid();
        var video = CreateReferenceVideo(Now.AddDays(-1), 100);
        video.IsReference = false;

        video.MarkAsReference(Now, actorUserId);

        Assert.Equal(actorUserId, video.ReferenceSetByUserId);
        Assert.Equal(actorUserId, video.ModifiedByUserId);
    }

    [Fact]
    public void Remove_reference_returns_a_young_video_to_active_and_preserves_counts_and_history()
    {
        var video = CreateReferenceVideo(publishedAt: Now.AddDays(-(Video.MatureAgeDays - 1)), views: 10_000_000);
        video.IsReferenceUsed = true;

        var removed = video.RemoveReference(Now, Guid.NewGuid());

        Assert.True(removed);
        Assert.False(video.IsReference);
        Assert.False(video.IsReferenceUsed);
        Assert.Null(video.ReferenceSetAt);
        Assert.Null(video.ReferenceSetByUserId);

        // The mature-stop rule needs BOTH age and views, so a young video is ACTIVE again.
        Assert.Equal(VideoTrackingStatus.ACTIVE, video.TrackingStatus);
        Assert.Equal(Now, video.ModifiedAt);

        // Counts, duration, publication time, availability, and the fetch time are untouched.
        Assert.Equal(10_000_000, video.CurrentViews);
        Assert.Equal(45, video.CurrentLikes);
        Assert.Equal(120_000, video.DurationMillis);
        Assert.Equal(Now.AddDays(-(Video.MatureAgeDays - 1)), video.PublishedAt);
        Assert.Equal(VideoAvailabilityStatus.AVAILABLE, video.AvailabilityStatus);
        Assert.Equal(Now.AddHours(-1), video.LastStatsFetchedAt);
    }

    [Fact]
    public void Remove_reference_records_the_actor_in_modified_audit_fields()
    {
        var actorUserId = Guid.NewGuid();
        var video = CreateReferenceVideo(Now.AddDays(-1), 100);

        Assert.True(video.RemoveReference(Now, actorUserId));

        Assert.Equal(actorUserId, video.ModifiedByUserId);
        Assert.Null(video.ReferenceSetByUserId);
    }

    [Fact]
    public void Remove_reference_leaves_a_mature_and_popular_video_stopped()
    {
        var video = CreateReferenceVideo(publishedAt: Now.AddDays(-Video.MatureAgeDays), views: Video.MatureViewCount);

        Assert.True(video.RemoveReference(Now, Guid.NewGuid()));

        Assert.False(video.IsReference);
        Assert.Equal(VideoTrackingStatus.STOPPED, video.TrackingStatus);
    }

    [Fact]
    public void Remove_reference_refuses_a_video_that_is_not_a_reference()
    {
        var video = CreateReferenceVideo(publishedAt: Now.AddDays(-1), views: 10);
        video.IsReference = false;
        var modifiedBefore = video.ModifiedAt;

        Assert.False(video.RemoveReference(Now, Guid.NewGuid()));

        // Nothing moved: the row keeps its own state and timestamp.
        Assert.False(video.IsReference);
        Assert.Equal(VideoTrackingStatus.STOPPED, video.TrackingStatus);
        Assert.Equal(modifiedBefore, video.ModifiedAt);
    }

    [Fact]
    public void Set_reference_used_updates_only_usage_and_modified_audit_fields()
    {
        var video = CreateReferenceVideo(Now.AddDays(-1), 100);
        var actorUserId = Guid.NewGuid();

        var changed = video.SetReferenceUsed(true, Now, actorUserId);

        Assert.True(changed);
        Assert.True(video.IsReference);
        Assert.True(video.IsReferenceUsed);
        Assert.Equal(Now, video.ModifiedAt);
        Assert.Equal(actorUserId, video.ModifiedByUserId);
        Assert.Equal(Now.AddDays(-1), video.ReferenceSetAt);
        Assert.Equal(VideoTrackingStatus.STOPPED, video.TrackingStatus);
    }

    [Fact]
    public void Set_reference_used_refuses_a_non_reference_without_mutation()
    {
        var video = CreateReferenceVideo(Now.AddDays(-1), 100);
        video.IsReference = false;
        var modifiedBefore = video.ModifiedAt;

        var changed = video.SetReferenceUsed(true, Now, Guid.NewGuid());

        Assert.False(changed);
        Assert.False(video.IsReferenceUsed);
        Assert.Equal(modifiedBefore, video.ModifiedAt);
    }

    private static Video CreateReferenceVideo(DateTimeOffset publishedAt, long views) => new()
    {
        Id = Guid.NewGuid(),
        YoutubeVideoId = "v-reference",
        ChannelId = Guid.NewGuid(),
        Title = "Saved clip",
        ThumbnailUrl = "https://example.com/v-reference.jpg",
        PublishedAt = publishedAt,
        DurationMillis = 120_000,
        CurrentViews = views,
        CurrentLikes = 45,
        IsReference = true,
        ReferenceSetAt = Now.AddDays(-1),
        ReferenceSetByUserId = Guid.NewGuid(),
        TrackingStatus = VideoTrackingStatus.STOPPED,
        AvailabilityStatus = VideoAvailabilityStatus.AVAILABLE,
        LastStatsFetchedAt = Now.AddHours(-1),
        IsDeleted = false,
        CreatedAt = publishedAt,
        ModifiedAt = Now.AddDays(-1)
    };
}
