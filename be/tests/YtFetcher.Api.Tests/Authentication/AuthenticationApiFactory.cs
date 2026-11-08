using Microsoft.AspNetCore.Hosting;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using YtFetcher.Application.Abstractions.Services;
using YtFetcher.Core.Settings;
using YtFetcher.Infrastructure.Persistence;

namespace YtFetcher.Api.Tests.Authentication;

/// <summary>
/// Auth-enabled API host: the real composition root, the real session issuer and validator, and the real user
/// repository over an in-memory database. Only Firebase verification is replaced, because it is the one
/// boundary that would otherwise need external credentials.
/// </summary>
internal sealed class AuthenticationApiFactory : TestApiFactory
{
    private readonly DbContextOptions<YtFetcherDbContext> options = new DbContextOptionsBuilder<YtFetcherDbContext>()
        .UseInMemoryDatabase($"yt-fetcher-auth-tests-{Guid.NewGuid():N}")
        .ConfigureWarnings(warnings => warnings.Ignore(InMemoryEventId.TransactionIgnoredWarning))
        .Options;

    internal FakeFirebaseIdentityVerifier FirebaseVerifier { get; } = new();

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        base.ConfigureWebHost(builder);

        builder.ConfigureServices(services =>
        {
            services.RemoveAll<YtFetcherDbContext>();
            services.AddScoped(_ => new YtFetcherDbContext(options));
            services.RemoveAll<IFirebaseIdentityVerifier>();
            services.AddSingleton<IFirebaseIdentityVerifier>(FirebaseVerifier);
            services.AddHostedService<GeneralSettingsSeeder>();
        });
    }

    internal async Task<T> WithDatabaseAsync<T>(Func<YtFetcherDbContext, Task<T>> action)
    {
        using var scope = Services.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<YtFetcherDbContext>();

        return await action(context);
    }

    internal Task WithDatabaseAsync(Func<YtFetcherDbContext, Task> action) =>
        WithDatabaseAsync(async context =>
        {
            await action(context);
            return true;
        });

    private sealed class GeneralSettingsSeeder(IServiceProvider services) : IHostedService
    {
        public async Task StartAsync(CancellationToken cancellationToken)
        {
            using var scope = services.CreateScope();
            var context = scope.ServiceProvider.GetRequiredService<YtFetcherDbContext>();

            if (await context.GeneralSettings.AnyAsync(settings => settings.Id == GeneralSettings.SingletonId, cancellationToken))
            {
                return;
            }

            var now = DateTimeOffset.UtcNow;
            context.GeneralSettings.Add(new GeneralSettings
            {
                QueriesDailyQuotaLimit = 10000,
                VideoBatchGetStatsDailyQuotaLimit = 10000,
                SearchQueriesDailyQuotaLimit = 100,
                DiscoveryMaxPagesPerKeywordPerRun = 1,
                MaxDailyApprovedChannels = 100,
                BackfillPageLimit = 3,
                BackfillDispatchBatchSize = 10,
                CreatedAt = now,
                ModifiedAt = now
            });
            await context.SaveChangesAsync(cancellationToken);
        }

        public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
    }
}
