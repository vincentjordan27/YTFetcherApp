namespace YtFetcher.Core.BackfillRuns;

/// <summary>
/// The provider quota bucket that caused a backfill page to stop for the current Pacific date. Member names
/// match the database check-constraint values exactly. Search is deliberately absent: no backfill page uses
/// the Search bucket.
/// </summary>
public enum QuotaLimitBucket
{
    /// <summary>The general Queries bucket, consumed by <c>playlistItems.list</c>.</summary>
    QUERIES,

    /// <summary>The Video Batch Stats bucket, consumed by <c>videos.batchGetStats</c>.</summary>
    VIDEO_BATCH_STATS
}
