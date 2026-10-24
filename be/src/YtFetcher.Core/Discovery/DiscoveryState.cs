using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using YtFetcher.Core.Auditing;

namespace YtFetcher.Core.Discovery;

/// <summary>
/// Per-keyword discovery cursor. Only the recent-mode fields are used by this slice.
/// </summary>
[Table("discovery_states")]
public sealed class DiscoveryState : AuditableEntity
{
    [Key]
    [Column("id", TypeName = "uuid")]
    public Guid Id { get; set; }

    [Column("keyword_id", TypeName = "uuid")]
    public Guid KeywordId { get; set; }

    [Column("last_recent_completed_at", TypeName = "timestamp with time zone")]
    public DateTimeOffset? LastRecentCompletedAt { get; set; }

    [Column("recent_window_start", TypeName = "timestamp with time zone")]
    public DateTimeOffset? RecentWindowStart { get; set; }

    [Column("recent_window_end", TypeName = "timestamp with time zone")]
    public DateTimeOffset? RecentWindowEnd { get; set; }

    [Column("recent_page_token", TypeName = "text")]
    public string? RecentPageToken { get; set; }

    /// <summary>
    /// Pacific quota date on which the current recent-page traversal reached its terminal page. A matching
    /// date prevents a same-day restart at page one; a later date naturally begins a new recent traversal.
    /// </summary>
    [Column("recent_completed_quota_date", TypeName = "date")]
    public DateOnly? RecentCompletedQuotaDate { get; set; }

    [Column("history_window_start", TypeName = "timestamp with time zone")]
    public DateTimeOffset? HistoryWindowStart { get; set; }

    [Column("history_window_end", TypeName = "timestamp with time zone")]
    public DateTimeOffset? HistoryWindowEnd { get; set; }

    [Column("history_page_token", TypeName = "text")]
    public string? HistoryPageToken { get; set; }

}
