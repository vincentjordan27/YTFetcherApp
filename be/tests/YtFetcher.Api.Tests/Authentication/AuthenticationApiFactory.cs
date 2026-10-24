using Microsoft.AspNetCore.Hosting;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using YtFetcher.Application.Abstractions.Services;
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
}
