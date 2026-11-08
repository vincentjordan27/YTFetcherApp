using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace YtFetcher.Core.Settings;

[Table("general_settings")]
public sealed class GeneralSettings
{
    public const short SingletonId = 1;

    [Key]
    [Column("id")]
    public short Id { get; set; } = SingletonId;

    [Column("queries_daily_quota_limit")]
    public int QueriesDailyQuotaLimit { get; set; }

    [Column("video_batch_get_stats_daily_quota_limit")]
    public int VideoBatchGetStatsDailyQuotaLimit { get; set; }

    [Column("search_queries_daily_quota_limit")]
    public int SearchQueriesDailyQuotaLimit { get; set; }

    [Column("discovery_max_pages_per_keyword_per_run")]
    public int DiscoveryMaxPagesPerKeywordPerRun { get; set; }

    [Column("max_daily_approved_channels")]
    public int MaxDailyApprovedChannels { get; set; }

    [Column("backfill_page_limit")]
    public int BackfillPageLimit { get; set; }

    [Column("backfill_dispatch_batch_size")]
    public int BackfillDispatchBatchSize { get; set; }

    [Column("minimum_subscriber_count")]
    public long? MinimumSubscriberCount { get; set; }

    [Column("created_at", TypeName = "timestamp with time zone")]
    public DateTimeOffset CreatedAt { get; set; }

    [Column("created_by", TypeName = "uuid")]
    public Guid? CreatedByUserId { get; set; }

    [Column("modified_at", TypeName = "timestamp with time zone")]
    public DateTimeOffset ModifiedAt { get; set; }

    [Column("modified_by", TypeName = "uuid")]
    public Guid? ModifiedByUserId { get; set; }
}
