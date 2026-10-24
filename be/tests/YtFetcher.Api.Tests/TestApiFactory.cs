using Hangfire;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using YtFetcher.Api.Tests.Backfill;
using YtFetcher.Api.Tests.Discovery;

namespace YtFetcher.Api.Tests;

/// <summary>
/// Shared API test host. It never starts a real Hangfire server, records recurring-job and enqueued
/// job registrations instead of writing them to Hangfire storage, and replaces the PostgreSQL job
/// storage with a stub so a Development host can start without a live database.
/// </summary>
public class TestApiFactory : WebApplicationFactory<Program>
{
    public RecordingRecurringJobManager RecurringJobs { get; } = new();

    public RecordingBackgroundJobClient BackgroundJobs { get; } = new();

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.ConfigureServices(services =>
        {
            RemoveHangfireServer(services);
            services.AddSingleton<IRecurringJobManager>(RecurringJobs);
            services.AddSingleton<IBackgroundJobClient>(BackgroundJobs);
            ReplaceHangfireStorage(services);
        });
    }

    private static void RemoveHangfireServer(IServiceCollection services)
    {
        var hangfireHostedServices = services
            .Where(descriptor => descriptor.ServiceType == typeof(IHostedService) &&
                                 IsHangfireImplementation(descriptor))
            .ToList();

        foreach (var descriptor in hangfireHostedServices)
        {
            services.Remove(descriptor);
        }
    }

    private static bool IsHangfireImplementation(ServiceDescriptor descriptor)
    {
        var fullName = descriptor.ImplementationType?.FullName ??
                       descriptor.ImplementationFactory?.Method.DeclaringType?.FullName;

        return fullName?.StartsWith("Hangfire.", StringComparison.Ordinal) == true;
    }

    private static void ReplaceHangfireStorage(IServiceCollection services)
    {
        // Hangfire's ThrowIfNotConfigured requires an IGlobalConfiguration, but the configuration
        // registered by AddInfrastructure builds a PostgreSQL storage that opens a connection in its
        // constructor. Register empty configuration and storage stubs so a Development host can
        // start without a live database.
        services.RemoveAll<IGlobalConfiguration>();
        services.AddSingleton<IGlobalConfiguration>(new StubGlobalConfiguration());
        services.RemoveAll<JobStorage>();
        services.AddSingleton<JobStorage>(new StubJobStorage());
    }
}
