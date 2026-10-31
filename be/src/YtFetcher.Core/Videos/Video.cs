using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using YtFetcher.Core.Auditing;
using YtFetcher.Core.Channels;

namespace YtFetcher.Core.Videos;

/// <summary>
/// Persisted short-form video ingested from an approved channel, plus its current statistics state.
/// </summary>
[Table("videos")]
public sealed class Video : AuditableEntity
{
    /// <summary>Longest duration the product treats as short-form, in milliseconds.</summary>
    public const long MaxShortFormDurationMillis = 180_000;

    /// <summary>Age at which a very popular video stops being polled, in days.</summary>
    public const int MatureAgeDays = 120;

    /// <summary>View count at which a mature video stops being polled.</summary>
    public const long MatureViewCount = 5_000_000;

    /// <summary>
    /// How long a durable statistics claim stays owned before the next daily processing phase may recover
    /// it. It bounds how long a crashed attempt can hide a due video from the next attempt.
    /// </summary>
    public static readonly TimeSpan StatsClaimLifetime = TimeSpan.FromMinutes(30);

    [Key]
    [Column("id", TypeName = "uuid")]
    public Guid Id { get; set; }

    [Required]
    [MaxLength(32)]
    [Column("youtube_video_id")]
    public string YoutubeVideoId { get; set; } = string.Empty;

    [Column("channel_id", TypeName = "uuid")]
    public Guid ChannelId { get; set; }

    [Required]
    [MaxLength(1000)]
    [Column("title")]
    public string Title { get; set; } = string.Empty;

    [Column("thumbnail_url", TypeName = "text")]
    public string? ThumbnailUrl { get; set; }

    [Column("published_at", TypeName = "timestamp with time zone")]
    public DateTimeOffset PublishedAt { get; set; }

    [Column("duration_millis")]
    public long DurationMillis { get; set; }

    [Column("current_views")]
    public long CurrentViews { get; set; }

    [Column("current_likes")]
    public long CurrentLikes { get; set; }

    [Column("is_reference")]
    public bool IsReference { get; set; }

    [Column("is_reference_used")]
    public bool IsReferenceUsed { get; set; }

    [Column("reference_set_by_user_id", TypeName = "uuid")]
    public Guid? ReferenceSetByUserId { get; set; }

    [Column("reference_set_at", TypeName = "timestamp with time zone")]
    public DateTimeOffset? ReferenceSetAt { get; set; }

    [Required]
    [MaxLength(20)]
    [Column("tracking_status")]
    public string TrackingStatusValue { get; private set; } = nameof(VideoTrackingStatus.ACTIVE);

    [NotMapped]
    public VideoTrackingStatus TrackingStatus
    {
        get => Enum.Parse<VideoTrackingStatus>(TrackingStatusValue, ignoreCase: false);
        set => TrackingStatusValue = value.ToString();
    }

    [Column("consecutive_failures")]
    public int ConsecutiveFailures { get; set; }

    [Column("last_stats_fetched_at", TypeName = "timestamp with time zone")]
    public DateTimeOffset? LastStatsFetchedAt { get; set; }

    /// <summary>
    /// Persisted provider availability. It never replaces <see cref="TrackingStatusValue"/>, because
    /// tracking is the polling lifecycle and availability is the retrievability reason.
    /// </summary>
    [Required]
    [MaxLength(20)]
    [Column("availability_status")]
    public string AvailabilityStatusValue { get; private set; } = nameof(VideoAvailabilityStatus.AVAILABLE);

    [NotMapped]
    public VideoAvailabilityStatus AvailabilityStatus
    {
        get => Enum.Parse<VideoAvailabilityStatus>(AvailabilityStatusValue, ignoreCase: false);
        set => AvailabilityStatusValue = value.ToString();
    }

    /// <summary>
    /// Durable statistics claim owned by one polling attempt. Only a persistence call carrying this exact
    /// token may apply or release the claim, so a stale worker can never overwrite a later attempt.
    /// </summary>
    [Column("stats_claim_token", TypeName = "uuid")]
    public Guid? StatsClaimToken { get; private set; }

    [Column("stats_claimed_at", TypeName = "timestamp with time zone")]
    public DateTimeOffset? StatsClaimedAt { get; private set; }

    /// <summary>True when this row is currently claimed by a polling attempt.</summary>
    [NotMapped]
    public bool IsStatisticsClaimed => StatsClaimToken is not null;

    /// <summary>
    /// Creates a newly ingested video with the M5 initial tracking state: a mature, very popular
    /// video starts stopped, everything else starts active and is eligible on the next Pacific daily window.
    /// </summary>
    public static Video CreateIngested(
        string youtubeVideoId,
        Guid channelId,
        string title,
        string? thumbnailUrl,
        DateTimeOffset publishedAt,
        long durationMillis,
        long viewCount,
        long likeCount,
        DateTimeOffset utcNow)
    {
        // A mature, already very popular video is not polled; everything else starts active.
        var isMatureAndPopular = publishedAt <= utcNow.AddDays(-MatureAgeDays) && viewCount >= MatureViewCount;

        return new Video
        {
            Id = Guid.NewGuid(),
            YoutubeVideoId = youtubeVideoId,
            ChannelId = channelId,
            Title = title,
            ThumbnailUrl = thumbnailUrl,
            PublishedAt = publishedAt,
            DurationMillis = durationMillis,
            CurrentViews = viewCount,
            CurrentLikes = likeCount,
            IsReference = false,
            IsDeleted = false,
            // Backfill ingestion always creates a retrievable video: availability is about the provider
            // dropping an item later, never about how it was first discovered.
            AvailabilityStatus = VideoAvailabilityStatus.AVAILABLE,
            TrackingStatus = isMatureAndPopular ? VideoTrackingStatus.STOPPED : VideoTrackingStatus.ACTIVE,
            CreatedAt = utcNow,
            ModifiedAt = utcNow,
            LastStatsFetchedAt = utcNow
        };
    }

    /// <summary>True when the video is short-form and therefore eligible for tracking.</summary>
    public static bool IsShortForm(long durationMillis) => durationMillis <= MaxShortFormDurationMillis;

    /// <summary>
    /// Applies the one-way reference transition: marks the video as reference, stops statistics
    /// tracking, and records the reference metadata. Title, channel, current views/likes, published
    /// timestamp, and the last stats fetch time are deliberately left unchanged.
    /// </summary>
    public void MarkAsReference(DateTimeOffset utcNow, Guid actorUserId)
    {
        IsReference = true;
        IsReferenceUsed = true;
        ReferenceSetAt = utcNow;
        ReferenceSetByUserId = actorUserId;
        TrackingStatus = VideoTrackingStatus.STOPPED;
        ModifiedAt = utcNow;
        ModifiedByUserId = actorUserId;
    }

    /// <summary>
    /// Removes the reference flag and re-decides tracking from the same mature-stop rule the ingestion and
    /// statistics paths use: a video that is already mature and very popular stays
    /// <see cref="VideoTrackingStatus.STOPPED"/>, everything else returns to
    /// <see cref="VideoTrackingStatus.ACTIVE"/> and becomes due on the next ordinary daily run.
    /// </summary>
    /// <remarks>
    /// The reference metadata is cleared together with the flag, keeping the invariant that a non-reference
    /// row never carries a reference timestamp; the backfill ingestion path creates non-reference rows the
    /// same way. Current views/likes, publication time, availability, the last statistics fetch and every
    /// snapshot row are deliberately untouched. Returns <c>false</c> without mutating state when the video
    /// is not currently a reference.
    /// </remarks>
    public bool RemoveReference(DateTimeOffset utcNow, Guid actorUserId)
    {
        if (!IsReference)
        {
            return false;
        }

        IsReference = false;
        IsReferenceUsed = false;
        ReferenceSetAt = null;
        ReferenceSetByUserId = null;

        var isMatureAndPopular = PublishedAt <= utcNow.AddDays(-MatureAgeDays) && CurrentViews >= MatureViewCount;
        TrackingStatus = isMatureAndPopular ? VideoTrackingStatus.STOPPED : VideoTrackingStatus.ACTIVE;

        ModifiedAt = utcNow;
        ModifiedByUserId = actorUserId;
        return true;
    }

    public bool SetReferenceUsed(bool isUsed, DateTimeOffset utcNow, Guid actorUserId)
    {
        if (!IsReference)
        {
            return false;
        }

        IsReferenceUsed = isUsed;
        ModifiedAt = utcNow;
        ModifiedByUserId = actorUserId;
        return true;
    }

    /// <summary>
    /// Takes the durable statistics claim for one polling attempt. The token is the ownership proof used by
    /// the success and release writes.
    /// </summary>
    public void ClaimForStatistics(Guid claimToken, DateTimeOffset utcNow)
    {
        StatsClaimToken = claimToken;
        StatsClaimedAt = utcNow;
    }

    /// <summary>
    /// Releases the claim without touching any statistics value or due time, which is what a failed batch and
    /// a recovered stale claim both need.
    /// </summary>
    public void ReleaseStatisticsClaim()
    {
        StatsClaimToken = null;
        StatsClaimedAt = null;
    }

    /// <summary>
    /// Applies one successful provider statistics row: fresh counts, both fetch instants, an available state,
    /// and the mature-stop rule. The claim is cleared here because it is released in the same transaction.
    /// </summary>
    public void ApplyStatistics(long viewCount, long likeCount, DateTimeOffset fetchedAt)
    {
        CurrentViews = viewCount;
        CurrentLikes = likeCount;
        LastStatsFetchedAt = fetchedAt;
        AvailabilityStatus = VideoAvailabilityStatus.AVAILABLE;

        if (PublishedAt <= fetchedAt.AddDays(-MatureAgeDays) && viewCount >= MatureViewCount)
        {
            // A mature, very popular video has stopped moving enough to be worth polling.
            TrackingStatus = VideoTrackingStatus.STOPPED;
        }

        ModifiedAt = fetchedAt;
        ReleaseStatisticsClaim();
    }

    /// <summary>
    /// Marks the video unavailable because a successful provider response did not return it. Counts are
    /// deliberately untouched: an absent resource must never be recorded as zero views.
    /// </summary>
    public void MarkUnavailable(DateTimeOffset utcNow)
    {
        AvailabilityStatus = VideoAvailabilityStatus.UNAVAILABLE;
        TrackingStatus = VideoTrackingStatus.STOPPED;
        ModifiedAt = utcNow;
        ReleaseStatisticsClaim();
    }
}
