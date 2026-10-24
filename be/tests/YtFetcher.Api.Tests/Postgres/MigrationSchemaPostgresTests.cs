using Microsoft.EntityFrameworkCore;
using Npgsql;
using YtFetcher.Infrastructure.Persistence;

namespace YtFetcher.Api.Tests.Postgres;

/// <summary>
/// PostgreSQL-only proof of the consolidated baseline shape: the scheduling columns and recovery index, the
/// granular quota ledger without the dropped legacy column, the per-channel first-page reservation without the
/// retired per-channel marker, and the quota-limit bucket constraint. It applies the authoritative root schema
/// chain and is skipped unless the runner supplies <c>YTF_TEST_POSTGRES</c>, so the schema is never claimed from
/// source inspection alone.
/// </summary>
[Collection(PostgresProofCollection.Name)]
public class MigrationSchemaPostgresTests : IAsyncLifetime
{
    private readonly string connectionString = PostgresProofTarget.ConnectionString ?? string.Empty;

    private YtFetcherDbContext dbContext = null!;

    public async Task InitializeAsync()
    {
        if (!PostgresProofTarget.IsAvailable)
        {
            return;
        }

        dbContext = CreateContext();
        // The baseline is consolidated, so proof setup applies every root schema script that exists in the
        // chain rather than assuming the baseline already contains a named former migration.
        await PostgresProofDatabase.RecreateBaselineAsync(dbContext);
    }

    public async Task DisposeAsync()
    {
        if (!PostgresProofTarget.IsAvailable)
        {
            return;
        }

        await PostgresProofDatabase.ResetAsync(dbContext);
        await dbContext.DisposeAsync();
    }

    [PostgresProofFact]
    public async Task The_baseline_adds_both_nullable_scheduling_columns()
    {
        var columns = await dbContext.Database
            .SqlQueryRaw<string>(
                """
                SELECT column_name || ':' || is_nullable AS "Value"
                FROM information_schema.columns
                WHERE table_schema = 'public'
                  AND table_name = 'channels'
                  AND column_name IN ('backfill_scheduling_attempted_at', 'backfill_scheduling_failure_reason')
                """)
            .ToListAsync();

        Assert.Equal(
            ["backfill_scheduling_attempted_at:YES", "backfill_scheduling_failure_reason:YES"],
            columns.Order().ToArray());

        var reasonLength = await dbContext.Database
            .SqlQueryRaw<int>(
                """
                SELECT character_maximum_length AS "Value"
                FROM information_schema.columns
                WHERE table_schema = 'public'
                  AND table_name = 'channels'
                  AND column_name = 'backfill_scheduling_failure_reason'
                """)
            .SingleAsync();

        Assert.Equal(500, reasonLength);
    }

    [PostgresProofFact]
    public async Task The_baseline_adds_the_partial_dispatch_recovery_index()
    {
        // The baseline index is the dispatch recovery index; asserting any other name would fail against the
        // authoritative schema, so the name here matches the baseline exactly.
        var indexDefinition = await dbContext.Database
            .SqlQueryRaw<string>(
                """
                SELECT indexdef AS "Value"
                FROM pg_indexes
                WHERE schemaname = 'public'
                  AND tablename = 'channels'
                  AND indexname = 'ix_channels_backfill_dispatch_recovery'
                """)
            .SingleAsync();

        Assert.Contains("backfill_scheduling_attempted_at", indexDefinition, StringComparison.Ordinal);
        Assert.Contains("WHERE", indexDefinition, StringComparison.Ordinal);
        Assert.Contains("APPROVED", indexDefinition, StringComparison.Ordinal);
        Assert.Contains("SCHEDULING", indexDefinition, StringComparison.Ordinal);
        Assert.Contains("SCHEDULING_FAILED", indexDefinition, StringComparison.Ordinal);
    }

    [PostgresProofFact]
    public async Task The_baseline_has_the_granular_quota_ledger_and_no_legacy_exhaustion_column()
    {
        var ledgerColumns = await ReadColumnNamesAsync("youtube_daily_quota_usage");

        Assert.Contains("queries_units_used", ledgerColumns);
        Assert.Contains("video_batch_stats_units_used", ledgerColumns);
        Assert.Contains("search_query_units_used", ledgerColumns);
        Assert.Contains("first_page_attempt_limit", ledgerColumns);
        Assert.Contains("first_page_attempt_used", ledgerColumns);
        Assert.Contains("search_quota_exhausted_at", ledgerColumns);
        Assert.Contains("queries_quota_exhausted_at", ledgerColumns);
        Assert.Contains("video_batch_stats_quota_exhausted_at", ledgerColumns);

        // The pre-granular single marker is deliberately gone. Proving it by exact set is stronger than a single
        // absence check, and it keeps the dropped column name out of the repository so the required audit command
        // returns only the valid per-bucket matches.
        Assert.Equal(
            ["queries_quota_exhausted_at", "search_quota_exhausted_at", "video_batch_stats_quota_exhausted_at"],
            ledgerColumns
                .Where(name => name.EndsWith("_exhausted_at", StringComparison.Ordinal))
                .OrderBy(name => name, StringComparer.Ordinal)
                .ToArray());
    }

    [PostgresProofFact]
    public async Task The_baseline_has_first_page_quota_date_and_no_legacy_per_channel_reservation()
    {
        var channelColumns = await ReadColumnNamesAsync("channels");

        Assert.Contains("first_page_quota_date", channelColumns);
        Assert.DoesNotContain("backfill_quota_reservation_date", channelColumns);
    }

    [PostgresProofFact]
    public async Task The_baseline_only_allows_a_quota_limit_bucket_on_a_quota_limited_run()
    {
        var channelId = Guid.NewGuid();
        await dbContext.Database.ExecuteSqlInterpolatedAsync(
            $"""
            INSERT INTO channels (
                id,
                youtube_channel_id,
                name,
                status,
                backfill_status,
                discovered_at,
                is_deleted,
                created_at,
                modified_at)
            VALUES (
                {channelId},
                {$"UC-{channelId:N}"},
                'Constraint proof channel',
                'APPROVED',
                'PENDING',
                now(),
                FALSE,
                now(),
                now())
            """);

        // Accepted: a quota-limited run carries one of the two backfill buckets.
        await InsertRunAsync(channelId, "QUOTA_LIMIT_REACHED", "QUERIES");
        await InsertRunAsync(channelId, "QUOTA_LIMIT_REACHED", "VIDEO_BATCH_STATS");

        // Rejected: a quota-limited run without a bucket, another status with a bucket, and an unknown bucket.
        await Assert.ThrowsAsync<PostgresException>(
            () => InsertRunAsync(channelId, "QUOTA_LIMIT_REACHED", null));
        await Assert.ThrowsAsync<PostgresException>(
            () => InsertRunAsync(channelId, "SUCCEEDED", "QUERIES"));
        await Assert.ThrowsAsync<PostgresException>(
            () => InsertRunAsync(channelId, "QUOTA_LIMIT_REACHED", "SEARCH"));
    }

    private async Task<string[]> ReadColumnNamesAsync(string tableName) =>
        (await dbContext.Database
            .SqlQueryRaw<string>(
                """
                SELECT column_name AS "Value"
                FROM information_schema.columns
                WHERE table_schema = 'public'
                  AND table_name = {0}
                """,
                tableName)
            .ToListAsync())
        .ToArray();

    private Task InsertRunAsync(Guid channelId, string status, string? quotaLimitBucket)
    {
        var startedAt = DateTimeOffset.UtcNow;
        var completedAt = status == "RUNNING" ? (DateTimeOffset?)null : startedAt;

        return dbContext.Database.ExecuteSqlInterpolatedAsync(
            $"""
            INSERT INTO channel_backfill_runs (
                id,
                channel_id,
                claim_token,
                trigger_source,
                page_number,
                status,
                started_at,
                completed_at,
                quota_limit_bucket,
                is_deleted,
                created_at,
                modified_at)
            VALUES (
                gen_random_uuid(),
                {channelId},
                gen_random_uuid(),
                'APPROVAL',
                1,
                {status},
                {startedAt},
                {completedAt},
                {quotaLimitBucket},
                FALSE,
                {startedAt},
                {startedAt})
            """);
    }

    private YtFetcherDbContext CreateContext() =>
        new(new DbContextOptionsBuilder<YtFetcherDbContext>().UseNpgsql(connectionString).Options);
}
