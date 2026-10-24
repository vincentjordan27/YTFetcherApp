using Microsoft.EntityFrameworkCore;
using YtFetcher.Application.Abstractions.Services;
using YtFetcher.Application.Quotas;
using YtFetcher.Core.Quotas;
using YtFetcher.Infrastructure.Persistence;
using YtFetcher.Infrastructure.Persistence.Repositories;

namespace YtFetcher.Api.Tests.Postgres;

/// <summary>
/// PostgreSQL-only proof for the Usage read: the three bucket counters are matched by the supplied Pacific
/// date and soft-deleted ledger rows are never returned. Skipped unless the runner supplies
/// <c>YTF_TEST_POSTGRES</c>.
/// </summary>
[Collection(PostgresProofCollection.Name)]
public class UsagePersistenceTests : IAsyncLifetime
{
    private static readonly DateTimeOffset Now = new(2026, 10, 1, 18, 0, 0, TimeSpan.Zero);

    private static readonly DateOnly QuotaDate = new(2026, 10, 1);

    private readonly string connectionString = PostgresProofTarget.ConnectionString ?? string.Empty;

    private YtFetcherDbContext dbContext = null!;

    public async Task InitializeAsync()
    {
        if (!PostgresProofTarget.IsAvailable)
        {
            return;
        }

        dbContext = CreateContext();
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
    public async Task Read_returns_the_active_row_for_its_date_and_never_the_soft_deleted_one()
    {
        await SeedLedgerRowAsync(isDeleted: true);
        await SeedLedgerRowAsync(isDeleted: false);

        var usage = await CreateRepository().GetForDateAsync(QuotaDate, CancellationToken.None);

        Assert.NotNull(usage);
        Assert.Equal(QuotaDate, usage!.QuotaDate);
        Assert.False(usage.IsDeleted);
        Assert.Equal(300, usage.QueriesUnitsUsed);
        Assert.Equal(400, usage.VideoBatchStatsUnitsUsed);
        Assert.Equal(12, usage.SearchQueryUnitsUsed);
        Assert.Equal(500, usage.FirstPageAttemptLimit);
        Assert.Equal(40, usage.FirstPageAttemptUsed);
    }

    [PostgresProofFact]
    public async Task Read_returns_null_when_only_a_soft_deleted_row_exists_for_the_date()
    {
        await SeedLedgerRowAsync(isDeleted: true);

        var usage = await CreateRepository().GetForDateAsync(QuotaDate, CancellationToken.None);

        Assert.Null(usage);
    }

    [PostgresProofFact]
    public async Task Read_returns_null_for_a_pacific_date_that_has_no_ledger_row()
    {
        await SeedLedgerRowAsync(isDeleted: false);

        var usage = await CreateRepository().GetForDateAsync(QuotaDate.AddDays(-1), CancellationToken.None);

        Assert.Null(usage);
    }

    private YtFetcherDbContext CreateContext() =>
        new(new DbContextOptionsBuilder<YtFetcherDbContext>().UseNpgsql(connectionString).Options);

    private DailyQuotaRepository CreateRepository() =>
        new(dbContext, new StubAppSettingsService(), TimeProvider.System);

    private async Task SeedLedgerRowAsync(bool isDeleted)
    {
        dbContext.YouTubeDailyQuotaUsages.Add(new YouTubeDailyQuotaUsage
        {
            Id = Guid.NewGuid(),
            QuotaDate = QuotaDate,
            QueriesUnitsUsed = isDeleted ? 999 : 300,
            VideoBatchStatsUnitsUsed = 400,
            SearchQueryUnitsUsed = 12,
            FirstPageAttemptLimit = 500,
            FirstPageAttemptUsed = 40,
            IsDeleted = isDeleted,
            DeletedAt = isDeleted ? Now : null,
            CreatedAt = Now,
            ModifiedAt = Now
        });

        await dbContext.SaveChangesAsync();
    }

    private sealed class StubAppSettingsService : IAppSettingsService
    {
        public string? GetValue(string key) => key switch
        {
            QuotaConfigurationKeys.QueriesDailyQuotaLimit => "10000",
            QuotaConfigurationKeys.VideoBatchGetStatsDailyQuotaLimit => "10000",
            QuotaConfigurationKeys.SearchQueriesDailyQuotaLimit => "100",
            QuotaConfigurationKeys.MaxDailyApprovedChannels => "500",
            _ => null
        };

        public string GetRequiredValue(string key) =>
            GetValue(key) ?? throw new InvalidOperationException($"Missing {key}.");

        public T GetRequiredSection<T>(string sectionName) where T : class => throw new NotSupportedException();
    }
}
