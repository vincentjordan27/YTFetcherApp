using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using YtFetcher.Core.Auditing;

namespace YtFetcher.Core.Videos;

/// <summary>
/// One captured statistics point for a video. M5 writes exactly one initial snapshot per new video.
/// </summary>
[Table("video_snapshots")]
public sealed class VideoSnapshot : AuditableEntity
{
    [Key]
    [Column("id")]
    public long Id { get; set; }

    [Column("video_id", TypeName = "uuid")]
    public Guid VideoId { get; set; }

    [Column("captured_at", TypeName = "timestamp with time zone")]
    public DateTimeOffset CapturedAt { get; set; }

    [Column("view_count")]
    public long ViewCount { get; set; }

    [Column("like_count")]
    public long LikeCount { get; set; }

    public static VideoSnapshot CreateInitial(Video video, DateTimeOffset utcNow) => new()
    {
        VideoId = video.Id,
        CapturedAt = utcNow,
        ViewCount = video.CurrentViews,
        LikeCount = video.CurrentLikes,
        IsDeleted = false,
        CreatedAt = utcNow,
        ModifiedAt = utcNow
    };
}
