using Microsoft.AspNetCore.Hosting;
using YtFetcher.Application.Quotas;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using YtFetcher.Api.Observability;
using YtFetcher.Core.Quotas;
using YtFetcher.Infrastructure.Observability;
using YtFetcher.Infrastructure.Persistence;

namespace YtFetcher.Api.Tests.Channels;

public sealed class ChannelApiFactory : TestApiFactory
{
    private readonly DbContextOptions<YtFetcherDbContext> options = new DbContextOptionsBuilder<YtFetcherDbContext>()
        .UseInMemoryDatabase($"yt-fetcher-tests-{Guid.NewGuid():N}")
        // The repositories own PostgreSQL transactions so concurrent state changes stay atomic. The
        // in-memory provider has no transactions, so its warning is ignored for these API tests; the
        // real locking and transaction behavior is proven by the PostgreSQL-only suite.
        .ConfigureWarnings(warnings => warnings.Ignore(InMemoryEventId.TransactionIgnoredWarning))
        .Options;

    public TestLogSink LogSink { get; } = new();

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        base.ConfigureWebHost(builder);

        builder.ConfigureServices(services =>
        {
            services.RemoveAll<YtFetcherDbContext>();
            services.AddScoped(_ => new YtFetcherDbContext(options));
            services.AddSingleton<ILogger<ChannelActivityLogger>>(LogSink.CreateLogger<ChannelActivityLogger>());
            services.AddSingleton<ILogger<UnhandledApiExceptionLoggingMiddleware>>(
                LogSink.CreateLogger<UnhandledApiExceptionLoggingMiddleware>());

            // Approval now claims a paired first-page slot that only exists once Phase 0 has snapshotted the
            // day's limit, so host tests seed a ready Pacific window like a normal daily run would.
            services.AddHostedService<QuotaWindowSeeder>();
        });
    }

    private sealed class QuotaWindowSeeder(IServiceProvider services) : IHostedService
    {
        public async Task StartAsync(CancellationToken cancellationToken)
        {
            using var scope = services.CreateScope();
            var context = scope.ServiceProvider.GetRequiredService<YtFetcherDbContext>();
            var now = DateTimeOffset.UtcNow;
            var quotaDate = PacificQuotaWindow.GetQuotaDate(now);

            if (await context.YouTubeDailyQuotaUsages.AnyAsync(usage => usage.QuotaDate == quotaDate, cancellationToken))
            {
                return;
            }

            context.YouTubeDailyQuotaUsages.Add(new YouTubeDailyQuotaUsage
            {
                Id = Guid.NewGuid(),
                QuotaDate = quotaDate,
                FirstPageAttemptLimit = 500,
                FirstPageAttemptUsed = 0,
                IsDeleted = false,
                CreatedAt = now,
                ModifiedAt = now
            });

            await context.SaveChangesAsync(cancellationToken);
        }

        public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
    }
}
