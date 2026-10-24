using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using YtFetcher.Core.Auditing;

namespace YtFetcher.Core.Discovery;

/// <summary>
/// Evidence that a specific video revealed a channel. The keyword records the first discovery source.
/// </summary>
[Table("channel_discoveries")]
public sealed class ChannelDiscovery : AuditableEntity
{
    [Key]
    [Column("id", TypeName = "uuid")]
    public Guid Id { get; set; }

    [Column("channel_id", TypeName = "uuid")]
    public Guid ChannelId { get; set; }

    [Column("keyword_id", TypeName = "uuid")]
    public Guid KeywordId { get; set; }

    [Required]
    [MaxLength(32)]
    [Column("youtube_video_id")]
    public string YoutubeVideoId { get; set; } = string.Empty;

    [Required]
    [MaxLength(1000)]
    [Column("video_title")]
    public string VideoTitle { get; set; } = string.Empty;

    [Column("video_thumbnail_url", TypeName = "text")]
    public string? VideoThumbnailUrl { get; set; }

    [Column("video_published_at", TypeName = "timestamp with time zone")]
    public DateTimeOffset VideoPublishedAt { get; set; }

    [Column("discovered_at", TypeName = "timestamp with time zone")]
    public DateTimeOffset DiscoveredAt { get; set; }
}
