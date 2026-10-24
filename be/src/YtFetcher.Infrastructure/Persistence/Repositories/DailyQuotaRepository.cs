using System.Globalization;
using Microsoft.EntityFrameworkCore;
using YtFetcher.Application.Abstractions.Repositories;
using YtFetcher.Application.Abstractions.Services;
using YtFetcher.Application.Quotas;
using YtFetcher.Application.Quotas.DTO.Result;
using YtFetcher.Core.Quotas;
using YtFetcher.Core.Users;
using YtFetcher.Infrastructure.Persistence;

namespace YtFetcher.Infrastructure.Persistence.Repositories;

/// <summary>
/// PostgreSQL daily provider-bucket ledger and durable owner of each Pacific date's daily processing run.
/// The relational path uses one atomic conditional statement per reservation, so a retry, a concurrent trigger,
/// or a provider quota refusal can never spend a unit twice or consume the capacity protected for unclaimed
/// first-page slots. The InMemory provider used by API host tests has no conditional UPDATE, so those methods
/// fall back to an equivalent tracked-entity path; that path is never used against PostgreSQL.
/// </summary>
public sealed class DailyQuotaRepository(
    YtFetcherDbContext dbContext,
    IAppSettingsService appSettingsService,
    TimeProvider timeProvider) : IDailyQuotaRepository
{
    public Task<YouTubeDailyQuotaUsage?> GetForDateAsync(
        DateOnly quotaDate,
        CancellationToken cancellationToken) =>
        dbContext.YouTubeDailyQuotaUsages
            .AsNoTracking()
            .SingleOrDefaultAsync(
                usage => usage.QuotaDate == quotaDate && !usage.IsDeleted,
                cancellationToken);

    public async Task EnsureLedgerAsync(DateOnly quotaDate, CancellationToken cancellationToken)
    {
        var firstPageAttemptLimit = ReadRequiredInt(QuotaConfigurationKeys.MaxDailyApprovedChannels);
        var utcNow = timeProvider.GetUtcNow();

        if (!dbContext.Database.IsRelational())
        {
            var tracked = await dbContext.YouTubeDailyQuotaUsages
                .FirstOrDefaultAsync(usage => usage.QuotaDate == quotaDate && !usage.IsDeleted, cancellationToken);

            if (tracked is null)
            {
                dbContext.YouTubeDailyQuotaUsages.Add(new YouTubeDailyQuotaUsage
                {
                    Id = Guid.NewGuid(),
                    QuotaDate = quotaDate,
                    FirstPageAttemptLimit = firstPageAttemptLimit,
                    IsDeleted = false,
                    CreatedAt = utcNow,
                    CreatedByUserId = User.SystemUserId,
                    ModifiedAt = utcNow,
                    ModifiedByUserId = User.SystemUserId
                });
                await dbContext.SaveChangesAsync(cancellationToken);
            }
            else if (tracked.FirstPageAttemptLimit == 0 && tracked.FirstPageAttemptUsed == 0)
            {
                tracked.FirstPageAttemptLimit = firstPageAttemptLimit;
                tracked.ModifiedAt = utcNow;
                tracked.ModifiedByUserId = User.SystemUserId;
                await dbContext.SaveChangesAsync(cancellationToken);
            }

            return;
        }

        // Create the date's row with the configured first-page cap snapshot. A row that already exists keeps
        // its committed value, so a retry of Phase 0 never resets a limit another run already used.
        await dbContext.Database.ExecuteSqlInterpolatedAsync(
            $"""
            INSERT INTO youtube_daily_quota_usage (
                id,
                quota_date,
                queries_units_used,
                video_batch_stats_units_used,
                search_query_units_used,
                first_page_attempt_limit,
                first_page_attempt_used,
                is_deleted,
                created_by,
                modified_by,
                created_at,
                modified_at)
            VALUES (
                gen_random_uuid(),
                {quotaDate},
                0,
                0,
                0,
                {firstPageAttemptLimit},
                0,
                FALSE,
                {User.SystemUserId},
                {User.SystemUserId},
                now(),
                now())
            ON CONFLICT DO NOTHING
            """,
            cancellationToken);

        // Repair a retained row whose window was never initialized, without ever moving a limit once a slot
        // has been claimed.
        await dbContext.YouTubeDailyQuotaUsages
            .Where(usage =>
                usage.QuotaDate == quotaDate &&
                !usage.IsDeleted &&
                usage.FirstPageAttemptLimit == 0 &&
                usage.FirstPageAttemptUsed == 0)
            .ExecuteUpdateAsync(
                setters => setters
                    .SetProperty(usage => usage.FirstPageAttemptLimit, _ => firstPageAttemptLimit)
                    .SetProperty(usage => usage.ModifiedAt, _ => utcNow)
                    .SetProperty(usage => usage.ModifiedByUserId, User.SystemUserId),
                cancellationToken);
    }

    public async Task<DailyProcessClaimDecision> TryClaimDailyProcessAsync(
        DateOnly quotaDate,
        Guid claimToken,
        DateTimeOffset utcNow,
        CancellationToken cancellationToken)
    {
        if (!dbContext.Database.IsRelational())
        {
            var tracked = await dbContext.YouTubeDailyQuotaUsages
                .FirstOrDefaultAsync(usage => usage.QuotaDate == quotaDate && !usage.IsDeleted, cancellationToken)
                ?? throw new InvalidOperationException("The daily ledger row does not exist.");

            var decision = tracked.TryClaimDailyProcessing(claimToken, utcNow);
            if (decision == DailyProcessClaimDecision.Claimed)
            {
                tracked.ModifiedByUserId = User.SystemUserId;
            }
            await dbContext.SaveChangesAsync(cancellationToken);
            return decision;
        }

        // One conditional statement is the decider: a completed date and a live claim both fail the predicate,
        // so a second runner cannot become a co-owner no matter how the two runs interleave.
        var claimed = await dbContext.YouTubeDailyQuotaUsages
            .Where(usage =>
                usage.QuotaDate == quotaDate &&
                !usage.IsDeleted &&
                usage.DailyProcessingCompletedAt == null &&
                (usage.DailyProcessingClaimToken == null ||
                 usage.DailyProcessingClaimedAt <= utcNow - YouTubeDailyQuotaUsage.DailyProcessClaimLifetime))
            .ExecuteUpdateAsync(
                setters => setters
                    .SetProperty(usage => usage.DailyProcessingClaimToken, _ => claimToken)
                    .SetProperty(usage => usage.DailyProcessingClaimedAt, _ => utcNow)
                    .SetProperty(usage => usage.ModifiedAt, _ => utcNow)
                    .SetProperty(usage => usage.ModifiedByUserId, User.SystemUserId),
                cancellationToken);

        if (claimed == 1)
        {
            return DailyProcessClaimDecision.Claimed;
        }

        var existing = await GetForDateAsync(quotaDate, cancellationToken);

        return existing is not null && !existing.HasLiveDailyProcessingClaim(utcNow)
            ? DailyProcessClaimDecision.AlreadyCompleted
            : DailyProcessClaimDecision.AlreadyRunning;
    }

    public async Task ReleaseDailyProcessClaimAsync(
        DateOnly quotaDate,
        Guid claimToken,
        CancellationToken cancellationToken)
    {
        var utcNow = timeProvider.GetUtcNow();

        if (!dbContext.Database.IsRelational())
        {
            var tracked = await dbContext.YouTubeDailyQuotaUsages
                .FirstOrDefaultAsync(
                    usage => usage.QuotaDate == quotaDate && !usage.IsDeleted && usage.DailyProcessingClaimToken == claimToken,
                    cancellationToken);

            if (tracked is not null)
            {
                tracked.ReleaseDailyProcessingClaim();
                tracked.ModifiedAt = utcNow;
                tracked.ModifiedByUserId = User.SystemUserId;
                await dbContext.SaveChangesAsync(cancellationToken);
            }

            return;
        }

        await dbContext.YouTubeDailyQuotaUsages
            .Where(usage =>
                usage.QuotaDate == quotaDate &&
                !usage.IsDeleted &&
                usage.DailyProcessingClaimToken == claimToken)
            .ExecuteUpdateAsync(
                setters => setters
                    .SetProperty(usage => usage.DailyProcessingClaimToken, _ => (Guid?)null)
                    .SetProperty(usage => usage.DailyProcessingClaimedAt, _ => (DateTimeOffset?)null)
                    .SetProperty(usage => usage.ModifiedAt, _ => utcNow)
                    .SetProperty(usage => usage.ModifiedByUserId, User.SystemUserId),
                cancellationToken);
    }

    public async Task<DailyFinalizationResult> FinalizeDailyProcessingAsync(
        DateOnly quotaDate,
        Guid claimToken,
        DateTimeOffset utcNow,
        CancellationToken cancellationToken)
    {
        var queriesLimit = ReadRequiredInt(QuotaConfigurationKeys.QueriesDailyQuotaLimit);
        var batchStatsLimit = ReadRequiredInt(QuotaConfigurationKeys.VideoBatchGetStatsDailyQuotaLimit);

        if (!dbContext.Database.IsRelational())
        {
            var tracked = await dbContext.YouTubeDailyQuotaUsages
                .FirstOrDefaultAsync(
                    usage => usage.QuotaDate == quotaDate &&
                             !usage.IsDeleted &&
                             usage.DailyProcessingClaimToken == claimToken &&
                             usage.DailyProcessingCompletedAt == null,
                    cancellationToken);

            if (tracked is null)
            {
                return new DailyFinalizationResult(false, false, 0, 0);
            }

            var gateOpened = !tracked.IsQueriesQuotaExhausted &&
                             !tracked.IsVideoBatchStatsQuotaExhausted &&
                             queriesLimit - tracked.QueriesUnitsUsed - tracked.UnclaimedFirstPageSlots > 0 &&
                             batchStatsLimit - tracked.VideoBatchStatsUnitsUsed - tracked.UnclaimedFirstPageSlots > 0;

            tracked.CompleteDailyProcessing(utcNow);
            if (gateOpened)
            {
                tracked.OpenBackfillDispatchGate(utcNow);
            }
            tracked.ModifiedByUserId = User.SystemUserId;

            await dbContext.SaveChangesAsync(cancellationToken);

            return new DailyFinalizationResult(
                true,
                tracked.IsBackfillDispatchOpen,
                queriesLimit - tracked.QueriesUnitsUsed - tracked.UnclaimedFirstPageSlots,
                batchStatsLimit - tracked.VideoBatchStatsUnitsUsed - tracked.UnclaimedFirstPageSlots);
        }

        // The gate decision and the completion write are one PostgreSQL statement, so the remainders are the
        // committed counters at that instant and no derived continuation allocation is persisted.
        var finalized = await dbContext.Database.ExecuteSqlInterpolatedAsync(
            $"""
            UPDATE youtube_daily_quota_usage
            SET daily_processing_completed_at = COALESCE(daily_processing_completed_at, {utcNow}),
                backfill_dispatch_opened_at = CASE
                    WHEN queries_quota_exhausted_at IS NULL
                     AND video_batch_stats_quota_exhausted_at IS NULL
                     AND {queriesLimit}
                         - queries_units_used
                         - (first_page_attempt_limit - first_page_attempt_used) > 0
                     AND {batchStatsLimit}
                         - video_batch_stats_units_used
                         - (first_page_attempt_limit - first_page_attempt_used) > 0
                    THEN COALESCE(backfill_dispatch_opened_at, {utcNow})
                    ELSE backfill_dispatch_opened_at
                END,
                daily_processing_claim_token = NULL,
                daily_processing_claimed_at = NULL,
                modified_at = now(),
                modified_by = {User.SystemUserId}
            WHERE quota_date = {quotaDate}
              AND is_deleted = FALSE
              AND daily_processing_claim_token = {claimToken}
              AND daily_processing_completed_at IS NULL
            """,
            cancellationToken);

        if (finalized != 1)
        {
            return new DailyFinalizationResult(false, false, 0, 0);
        }

        var ledger = await GetForDateAsync(quotaDate, cancellationToken);
        if (ledger is null)
        {
            return new DailyFinalizationResult(true, false, 0, 0);
        }

        var unclaimedFirstPageSlots = ledger.UnclaimedFirstPageSlots;

        return new DailyFinalizationResult(
            true,
            ledger.IsBackfillDispatchOpen,
            queriesLimit - ledger.QueriesUnitsUsed - unclaimedFirstPageSlots,
            batchStatsLimit - ledger.VideoBatchStatsUnitsUsed - unclaimedFirstPageSlots);
    }

    public async Task<QuotaReservationResult> TryReserveQueriesUnitAsync(
        DateOnly quotaDate,
        Guid? dailyProcessClaimToken,
        CancellationToken cancellationToken)
    {
        var queriesLimit = ReadRequiredInt(QuotaConfigurationKeys.QueriesDailyQuotaLimit);

        if (!dbContext.Database.IsRelational())
        {
            return await TrackedReserveAsync(
                quotaDate,
                dailyProcessClaimToken,
                usage => usage.QueriesQuotaExhaustedAt,
                usage => usage.QueriesUnitsUsed + usage.UnclaimedFirstPageSlots < queriesLimit,
                usage => usage.QueriesUnitsUsed += 1,
                cancellationToken);
        }

        var utcNow = timeProvider.GetUtcNow();
        var reserved = await dbContext.YouTubeDailyQuotaUsages
            .Where(usage =>
                usage.QuotaDate == quotaDate &&
                !usage.IsDeleted &&
                (dailyProcessClaimToken == null || usage.DailyProcessingClaimToken == dailyProcessClaimToken) &&
                (dailyProcessClaimToken == null || usage.DailyProcessingCompletedAt == null) &&
                usage.FirstPageAttemptLimit > 0 &&
                usage.QueriesQuotaExhaustedAt == null &&
                usage.QueriesUnitsUsed + (usage.FirstPageAttemptLimit - usage.FirstPageAttemptUsed) < queriesLimit)
            .ExecuteUpdateAsync(
                setters => setters
                    .SetProperty(usage => usage.QueriesUnitsUsed, usage => usage.QueriesUnitsUsed + 1)
                    .SetProperty(usage => usage.ModifiedAt, _ => utcNow)
                    .SetProperty(usage => usage.ModifiedByUserId, User.SystemUserId),
                cancellationToken);

        return reserved == 1
            ? QuotaReservationResult.Reserved
            : ClassifyRefusal(
                await GetForDateAsync(quotaDate, cancellationToken),
                dailyProcessClaimToken,
                static usage => usage.QueriesQuotaExhaustedAt);
    }

    public async Task<QuotaReservationResult> TryReserveVideoBatchStatsUnitAsync(
        DateOnly quotaDate,
        Guid? dailyProcessClaimToken,
        CancellationToken cancellationToken)
    {
        var batchStatsLimit = ReadRequiredInt(QuotaConfigurationKeys.VideoBatchGetStatsDailyQuotaLimit);

        if (!dbContext.Database.IsRelational())
        {
            return await TrackedReserveAsync(
                quotaDate,
                dailyProcessClaimToken,
                usage => usage.VideoBatchStatsQuotaExhaustedAt,
                usage => usage.VideoBatchStatsUnitsUsed + usage.UnclaimedFirstPageSlots < batchStatsLimit,
                usage => usage.VideoBatchStatsUnitsUsed += 1,
                cancellationToken);
        }

        var utcNow = timeProvider.GetUtcNow();
        var reserved = await dbContext.YouTubeDailyQuotaUsages
            .Where(usage =>
                usage.QuotaDate == quotaDate &&
                !usage.IsDeleted &&
                (dailyProcessClaimToken == null || usage.DailyProcessingClaimToken == dailyProcessClaimToken) &&
                (dailyProcessClaimToken == null || usage.DailyProcessingCompletedAt == null) &&
                usage.FirstPageAttemptLimit > 0 &&
                usage.VideoBatchStatsQuotaExhaustedAt == null &&
                usage.VideoBatchStatsUnitsUsed + (usage.FirstPageAttemptLimit - usage.FirstPageAttemptUsed) < batchStatsLimit)
            .ExecuteUpdateAsync(
                setters => setters
                    .SetProperty(usage => usage.VideoBatchStatsUnitsUsed, usage => usage.VideoBatchStatsUnitsUsed + 1)
                    .SetProperty(usage => usage.ModifiedAt, _ => utcNow)
                    .SetProperty(usage => usage.ModifiedByUserId, User.SystemUserId),
                cancellationToken);

        return reserved == 1
            ? QuotaReservationResult.Reserved
            : ClassifyRefusal(
                await GetForDateAsync(quotaDate, cancellationToken),
                dailyProcessClaimToken,
                static usage => usage.VideoBatchStatsQuotaExhaustedAt);
    }

    public async Task<QuotaReservationResult> TryReserveSearchUnitAsync(
        DateOnly quotaDate,
        Guid? dailyProcessClaimToken,
        CancellationToken cancellationToken)
    {
        var searchLimit = ReadRequiredInt(QuotaConfigurationKeys.SearchQueriesDailyQuotaLimit);

        if (!dbContext.Database.IsRelational())
        {
            return await TrackedReserveAsync(
                quotaDate,
                dailyProcessClaimToken,
                usage => usage.SearchQuotaExhaustedAt,
                usage => usage.SearchQueryUnitsUsed < searchLimit,
                usage => usage.SearchQueryUnitsUsed += 1,
                cancellationToken);
        }

        var utcNow = timeProvider.GetUtcNow();
        var reserved = await dbContext.YouTubeDailyQuotaUsages
            .Where(usage =>
                usage.QuotaDate == quotaDate &&
                !usage.IsDeleted &&
                (dailyProcessClaimToken == null || usage.DailyProcessingClaimToken == dailyProcessClaimToken) &&
                (dailyProcessClaimToken == null || usage.DailyProcessingCompletedAt == null) &&
                usage.FirstPageAttemptLimit > 0 &&
                usage.SearchQuotaExhaustedAt == null &&
                usage.SearchQueryUnitsUsed < searchLimit)
            .ExecuteUpdateAsync(
                setters => setters
                    .SetProperty(usage => usage.SearchQueryUnitsUsed, usage => usage.SearchQueryUnitsUsed + 1)
                    .SetProperty(usage => usage.ModifiedAt, _ => utcNow)
                    .SetProperty(usage => usage.ModifiedByUserId, User.SystemUserId),
                cancellationToken);

        return reserved == 1
            ? QuotaReservationResult.Reserved
            : ClassifyRefusal(
                await GetForDateAsync(quotaDate, cancellationToken),
                dailyProcessClaimToken,
                static usage => usage.SearchQuotaExhaustedAt);
    }

    public Task MarkSearchQuotaExhaustedAsync(DateOnly quotaDate, DateTimeOffset utcNow, CancellationToken cancellationToken) =>
        MarkAsync(quotaDate, usage => usage.MarkSearchQuotaExhausted(utcNow), cancellationToken);

    public Task MarkQueriesQuotaExhaustedAsync(DateOnly quotaDate, DateTimeOffset utcNow, CancellationToken cancellationToken) =>
        MarkAsync(quotaDate, usage => usage.MarkQueriesQuotaExhausted(utcNow), cancellationToken);

    public Task MarkVideoBatchStatsQuotaExhaustedAsync(DateOnly quotaDate, DateTimeOffset utcNow, CancellationToken cancellationToken) =>
        MarkAsync(quotaDate, usage => usage.MarkVideoBatchStatsQuotaExhausted(utcNow), cancellationToken);

    private async Task<QuotaReservationResult> TrackedReserveAsync(
        DateOnly quotaDate,
        Guid? dailyProcessClaimToken,
        Func<YouTubeDailyQuotaUsage, DateTimeOffset?> exhaustedMarker,
        Func<YouTubeDailyQuotaUsage, bool> hasCapacity,
        Action<YouTubeDailyQuotaUsage> increment,
        CancellationToken cancellationToken)
    {
        var tracked = await dbContext.YouTubeDailyQuotaUsages
            .FirstOrDefaultAsync(usage => usage.QuotaDate == quotaDate && !usage.IsDeleted, cancellationToken);

        if (dailyProcessClaimToken is { } token &&
            (tracked is null || tracked.DailyProcessingClaimToken != token || tracked.IsDailyProcessingCompleted))
        {
            return QuotaReservationResult.OwnershipLost;
        }

        if (tracked is null || !tracked.IsFirstPageWindowReady)
        {
            return QuotaReservationResult.WindowNotReady;
        }

        if (exhaustedMarker(tracked) is not null)
        {
            return QuotaReservationResult.QuotaExhausted;
        }

        if (!hasCapacity(tracked))
        {
            return QuotaReservationResult.NoRemainingCapacity;
        }

        increment(tracked);
        tracked.ModifiedAt = timeProvider.GetUtcNow();
        tracked.ModifiedByUserId = User.SystemUserId;
        await dbContext.SaveChangesAsync(cancellationToken);
        return QuotaReservationResult.Reserved;
    }

    /// <summary>
    /// Marks one bucket exhausted. A timestamp write is a simple tracked-entity update on both providers, so it
    /// does not need a conditional statement.
    /// </summary>
    private async Task MarkAsync(
        DateOnly quotaDate,
        Action<YouTubeDailyQuotaUsage> mark,
        CancellationToken cancellationToken)
    {
        var tracked = await dbContext.YouTubeDailyQuotaUsages
            .FirstOrDefaultAsync(usage => usage.QuotaDate == quotaDate && !usage.IsDeleted, cancellationToken);

        if (tracked is not null)
        {
            mark(tracked);
            tracked.ModifiedByUserId = User.SystemUserId;
            await dbContext.SaveChangesAsync(cancellationToken);
        }
    }

    private static QuotaReservationResult ClassifyRefusal(
        YouTubeDailyQuotaUsage? usage,
        Guid? dailyProcessClaimToken,
        Func<YouTubeDailyQuotaUsage, DateTimeOffset?> exhaustedMarker)
    {
        // Ownership is checked first: a fenced caller that no longer owns the date must be told apart from a
        // bucket that merely ran out, so the daily run can stop without claiming a quota limit it did not hit.
        if (dailyProcessClaimToken is { } token &&
            (usage is null || usage.DailyProcessingClaimToken != token || usage.IsDailyProcessingCompleted))
        {
            return QuotaReservationResult.OwnershipLost;
        }

        if (usage is null || !usage.IsFirstPageWindowReady)
        {
            return QuotaReservationResult.WindowNotReady;
        }

        return exhaustedMarker(usage) is not null
            ? QuotaReservationResult.QuotaExhausted
            : QuotaReservationResult.NoRemainingCapacity;
    }

    private int ReadRequiredInt(string key)
    {
        var value = appSettingsService.GetValue(key);

        return int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsed)
            ? parsed
            : throw new InvalidOperationException(
                $"Configuration value '{key}' is missing or not a whole number.");
    }
}
