using YtFetcher.Core.Videos;

namespace YtFetcher.Api.Tests.Core;

public class VideoReferenceDomainTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 1, 12, 0, 0, TimeSpan.Zero);

    [Theory]
    [InlineData(VideoReferenceStatus.UNUSED)]
    [InlineData(VideoReferenceStatus.SKIPPED)]
    public void Active_video_can_be_classified_as_unused_or_skipped(VideoReferenceStatus status)
    {
        var video = CreateVideo(VideoReferenceStatus.NONE);
        var actor = Guid.NewGuid();

        Assert.True(video.ClassifyForReference(status, Now, actor));

        Assert.Equal(status, video.ReferenceStatus);
        Assert.Equal(Now, video.ReferenceSetAt);
        Assert.Equal(actor, video.ReferenceSetByUserId);
        Assert.Equal(VideoTrackingStatus.STOPPED, video.TrackingStatus);
        Assert.Equal(actor, video.ModifiedByUserId);
    }

    [Theory]
    [InlineData(VideoReferenceStatus.NONE)]
    [InlineData(VideoReferenceStatus.USED)]
    public void Active_video_rejects_classifications_other_than_unused_or_skipped(VideoReferenceStatus status)
    {
        var video = CreateVideo(VideoReferenceStatus.NONE);
        var before = video.ModifiedAt;

        Assert.False(video.ClassifyForReference(status, Now, Guid.NewGuid()));

        Assert.Equal(VideoReferenceStatus.NONE, video.ReferenceStatus);
        Assert.Null(video.ReferenceSetAt);
        Assert.Equal(before, video.ModifiedAt);
    }

    [Fact]
    public void Stopped_video_cannot_be_classified_as_reference()
    {
        var video = CreateVideo(VideoReferenceStatus.NONE);
        video.TrackingStatus = VideoTrackingStatus.STOPPED;

        Assert.False(video.ClassifyForReference(VideoReferenceStatus.UNUSED, Now, Guid.NewGuid()));
        Assert.Equal(VideoReferenceStatus.NONE, video.ReferenceStatus);
    }

    [Theory]
    [InlineData(VideoReferenceStatus.UNUSED, VideoReferenceStatus.USED)]
    [InlineData(VideoReferenceStatus.UNUSED, VideoReferenceStatus.SKIPPED)]
    [InlineData(VideoReferenceStatus.USED, VideoReferenceStatus.UNUSED)]
    [InlineData(VideoReferenceStatus.USED, VideoReferenceStatus.SKIPPED)]
    [InlineData(VideoReferenceStatus.SKIPPED, VideoReferenceStatus.UNUSED)]
    public void Reference_video_can_follow_each_permitted_status_transition(
        VideoReferenceStatus initial, VideoReferenceStatus next)
    {
        var video = CreateVideo(initial);
        var referenceAt = video.ReferenceSetAt;
        var referenceBy = video.ReferenceSetByUserId;
        var actor = Guid.NewGuid();

        Assert.True(video.ChangeReferenceStatus(next, Now, actor));

        Assert.Equal(next, video.ReferenceStatus);
        Assert.Equal(referenceAt, video.ReferenceSetAt);
        Assert.Equal(referenceBy, video.ReferenceSetByUserId);
        Assert.Equal(VideoTrackingStatus.STOPPED, video.TrackingStatus);
        Assert.Equal(actor, video.ModifiedByUserId);
    }

    [Theory]
    [InlineData(VideoReferenceStatus.NONE, VideoReferenceStatus.NONE)]
    [InlineData(VideoReferenceStatus.NONE, VideoReferenceStatus.UNUSED)]
    [InlineData(VideoReferenceStatus.NONE, VideoReferenceStatus.USED)]
    [InlineData(VideoReferenceStatus.NONE, VideoReferenceStatus.SKIPPED)]
    [InlineData(VideoReferenceStatus.UNUSED, VideoReferenceStatus.UNUSED)]
    [InlineData(VideoReferenceStatus.UNUSED, VideoReferenceStatus.NONE)]
    [InlineData(VideoReferenceStatus.USED, VideoReferenceStatus.NONE)]
    [InlineData(VideoReferenceStatus.USED, VideoReferenceStatus.USED)]
    [InlineData(VideoReferenceStatus.SKIPPED, VideoReferenceStatus.SKIPPED)]
    [InlineData(VideoReferenceStatus.SKIPPED, VideoReferenceStatus.USED)]
    [InlineData(VideoReferenceStatus.SKIPPED, VideoReferenceStatus.NONE)]
    public void Reference_video_rejects_disallowed_status_transitions(
        VideoReferenceStatus initial, VideoReferenceStatus next)
    {
        var video = CreateVideo(initial);
        var before = video.ModifiedAt;

        Assert.False(video.ChangeReferenceStatus(next, Now, Guid.NewGuid()));

        Assert.Equal(initial, video.ReferenceStatus);
        Assert.Equal(before, video.ModifiedAt);
    }

    [Theory]
    [InlineData(VideoReferenceStatus.UNUSED)]
    [InlineData(VideoReferenceStatus.USED)]
    [InlineData(VideoReferenceStatus.SKIPPED)]
    public void Returning_any_reference_status_to_active_clears_metadata_and_restores_tracking(
        VideoReferenceStatus status)
    {
        var video = CreateVideo(status, Now.AddDays(-(Video.MatureAgeDays - 1)), Video.MatureViewCount);
        var actor = Guid.NewGuid();

        Assert.True(video.RemoveReference(Now, actor));

        Assert.Equal(VideoReferenceStatus.NONE, video.ReferenceStatus);
        Assert.Null(video.ReferenceSetAt);
        Assert.Null(video.ReferenceSetByUserId);
        Assert.Equal(VideoTrackingStatus.ACTIVE, video.TrackingStatus);
        Assert.Equal(actor, video.ModifiedByUserId);
    }

    [Fact]
    public void Returning_mature_popular_reference_to_active_keeps_tracking_stopped()
    {
        var video = CreateVideo(VideoReferenceStatus.SKIPPED, Now.AddDays(-Video.MatureAgeDays), Video.MatureViewCount);

        Assert.True(video.RemoveReference(Now, Guid.NewGuid()));

        Assert.Equal(VideoReferenceStatus.NONE, video.ReferenceStatus);
        Assert.Equal(VideoTrackingStatus.STOPPED, video.TrackingStatus);
    }

    [Fact]
    public void Returning_active_video_to_active_is_rejected_without_mutation()
    {
        var video = CreateVideo(VideoReferenceStatus.NONE);
        var modifiedAt = video.ModifiedAt;

        Assert.False(video.RemoveReference(Now, Guid.NewGuid()));

        Assert.Equal(VideoReferenceStatus.NONE, video.ReferenceStatus);
        Assert.Equal(modifiedAt, video.ModifiedAt);
    }

    private static Video CreateVideo(VideoReferenceStatus status, DateTimeOffset? publishedAt = null, long views = 100) => new()
    {
        Id = Guid.NewGuid(),
        YoutubeVideoId = "v-reference",
        ChannelId = Guid.NewGuid(),
        Title = "Saved clip",
        PublishedAt = publishedAt ?? Now.AddDays(-1),
        DurationMillis = 120_000,
        CurrentViews = views,
        CurrentLikes = 45,
        ReferenceStatus = status,
        ReferenceSetAt = status == VideoReferenceStatus.NONE ? null : Now.AddDays(-1),
        ReferenceSetByUserId = status == VideoReferenceStatus.NONE ? null : Guid.NewGuid(),
        TrackingStatus = status == VideoReferenceStatus.NONE ? VideoTrackingStatus.ACTIVE : VideoTrackingStatus.STOPPED,
        AvailabilityStatus = VideoAvailabilityStatus.AVAILABLE,
        ModifiedAt = Now.AddDays(-1)
    };
}
