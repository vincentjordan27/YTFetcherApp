using Microsoft.EntityFrameworkCore;
using YtFetcher.Application.Abstractions.Services;
using YtFetcher.Application.Quotas;
using YtFetcher.Core.Users;
using YtFetcher.Infrastructure.Persistence;
using YtFetcher.Infrastructure.Persistence.Repositories;

namespace YtFetcher.Api.Tests.Persistence;

public class DailyQuotaRepositoryAuditActorTests
{
    private static readonly DateOnly QuotaDate = new(2026, 10, 6);

    private static readonly DateTimeOffset Now = new(2026, 10, 6, 8, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task Ensure_and_claim_ledger_attribute_created_and_modified_audit_to_system()
    {
        await using var context = new YtFetcherDbContext(new DbContextOptionsBuilder<YtFetcherDbContext>()
            .UseInMemoryDatabase($"yt-fetcher-quota-audit-{Guid.NewGuid():N}")
            .Options);
        var repository = new DailyQuotaRepository(context, new StubGeneralSettingsProvider(), TimeProvider.System);

        await repository.EnsureLedgerAsync(QuotaDate, CancellationToken.None);

        var created = await context.YouTubeDailyQuotaUsages.AsNoTracking().SingleAsync();
        Assert.Equal(User.SystemUserId, created.CreatedByUserId);
        Assert.Equal(User.SystemUserId, created.ModifiedByUserId);

        await repository.TryClaimDailyProcessAsync(QuotaDate, Guid.NewGuid(), Now, CancellationToken.None);

        var claimed = await context.YouTubeDailyQuotaUsages.AsNoTracking().SingleAsync();
        Assert.Equal(User.SystemUserId, claimed.ModifiedByUserId);
    }

    private sealed class StubGeneralSettingsProvider : IGeneralSettingsProvider
    {
        public Task<YtFetcher.Application.Settings.GeneralSettingsSnapshot> GetRequiredAsync(CancellationToken cancellationToken) =>
            Task.FromResult(YtFetcher.Application.Settings.GeneralSettingsSnapshot.Default with { MaxDailyApprovedChannels = 500 });
    }
}
