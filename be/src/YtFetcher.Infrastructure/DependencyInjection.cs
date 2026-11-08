using Hangfire.PostgreSql;
using Hangfire;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;
using YtFetcher.Application.Abstractions.Repositories;
using YtFetcher.Application.Abstractions.Services;
using YtFetcher.Application.Quotas;
using YtFetcher.Infrastructure.Authentication;
using YtFetcher.Infrastructure.Configuration;
using YtFetcher.Infrastructure.Firebase;
using YtFetcher.Infrastructure.Health;
using YtFetcher.Infrastructure.Jobs;
using YtFetcher.Infrastructure.Observability;
using YtFetcher.Infrastructure.Persistence.Repositories;
using YtFetcher.Infrastructure.Persistence;
using YtFetcher.Infrastructure.Services;
using YtFetcher.Infrastructure.YouTube;

namespace YtFetcher.Infrastructure;

public static class DependencyInjection
{
    public static IServiceCollection AddInfrastructure(
        this IServiceCollection services,
        IConfiguration configuration,
        AuthenticationRuntimeConfiguration authenticationConfiguration)
    {
        services.AddSingleton<IAppSettingsService, AppSettingsService>();
        services.AddScoped<IHealthService, HealthService>();
        services.AddScoped<IChannelRepository, ChannelRepository>();
        services.AddScoped<IChannelActivityLogger, ChannelActivityLogger>();
        services.AddScoped<IDiscoveryRepository, DiscoveryRepository>();
        services.AddScoped<IDiscoveryKeywordRepository, DiscoveryKeywordRepository>();
        services.AddScoped<IDiscoveryActivityLogger, DiscoveryActivityLogger>();
        services.AddScoped<IBackfillRepository, BackfillRepository>();
        services.AddScoped<IBackfillRunRepository, BackfillRunRepository>();
        services.AddScoped<IDailyQuotaRepository, DailyQuotaRepository>();
        services.AddScoped<IGeneralSettingsRepository, GeneralSettingsRepository>();
        services.AddScoped<IGeneralSettingsProvider, GeneralSettingsProvider>();
        services.AddScoped<INewUploadMonitoringRepository, NewUploadMonitoringRepository>();
        services.AddScoped<IVideoRepository, VideoRepository>();
        services.AddScoped<IVideoStatisticsRepository, VideoStatisticsRepository>();
        services.AddScoped<IBackfillActivityLogger, BackfillActivityLogger>();
        services.AddScoped<INewUploadMonitorActivityLogger, NewUploadMonitorActivityLogger>();
        services.AddScoped<IBackfillScheduler, HangfireBackfillScheduler>();
        services.AddTransient<ChannelDiscoveryHangfireJob>();
        services.AddTransient<ChannelBackfillHangfireJob>();
        services.AddTransient<DailyProcessingHangfireJob>();
        services.AddTransient<BackfillClearHistoryHangfireJob>();
        services.TryAddSingleton(TimeProvider.System);

        // The authentication mode is a startup decision. The composition root validates it before the host is
        // built, so a Production instance with a missing or invalid Auth:Enabled stops with a readable error
        // instead of starting anonymously. A disabled instance registers no Firebase or JWT service at all.
        services.AddSingleton<IAuthenticationRuntimeConfiguration>(authenticationConfiguration);

        if (authenticationConfiguration.AuthenticationEnabled)
        {
            services.AddSingleton(authenticationConfiguration);
            services.AddScoped<IUserRepository, UserRepository>();
            services.AddSingleton<IApplicationSessionTokenIssuer, ApplicationSessionTokenIssuer>();
            services.AddSingleton<IApplicationSessionTokenValidator, ApplicationSessionTokenValidator>();
            services.AddSingleton<IFirebaseIdentityVerifier, FirebaseIdentityVerifier>();
        }

        var databaseOptions = configuration
            .GetSection(DatabaseOptions.SectionName)
            .Get<DatabaseOptions>()
            ?? throw new InvalidOperationException("Database configuration section is missing.");

        ValidateDatabaseOptions(databaseOptions);

        var connectionString = new NpgsqlConnectionStringBuilder
        {
            Host = databaseOptions.Host,
            Port = databaseOptions.Port,
            Database = databaseOptions.Name,
            Username = databaseOptions.Username,
            Password = databaseOptions.Password
        }.ConnectionString;

        services.AddDbContext<YtFetcherDbContext>(options =>
            options.UseNpgsql(connectionString));

        services.AddHttpClient<IYouTubeChannelDiscoveryGateway, YouTubeChannelDiscoveryClient>(client =>
            {
                client.BaseAddress = new Uri("https://www.googleapis.com/youtube/v3/");
            })
            .RemoveAllLoggers();

        services.AddHttpClient<IYouTubeBackfillGateway, YouTubeBackfillClient>(client =>
            {
                client.BaseAddress = new Uri("https://www.googleapis.com/youtube/v3/");
            })
            .RemoveAllLoggers();

        services.AddHttpClient<IYouTubeChannelLookupGateway, YouTubeChannelLookupClient>(client =>
            {
                client.BaseAddress = new Uri("https://www.googleapis.com/youtube/v3/");
            })
            .RemoveAllLoggers();

        services.AddHangfire(configuration => configuration
            .SetDataCompatibilityLevel(CompatibilityLevel.Version_180)
            .UseSimpleAssemblyNameTypeSerializer()
            .UseRecommendedSerializerSettings()
            .UsePostgreSqlStorage(options => options.UseNpgsqlConnection(connectionString)));

        return services;
    }

    private static void ValidateDatabaseOptions(DatabaseOptions options)
    {
        if (string.IsNullOrWhiteSpace(options.Host) ||
            options.Port is < 1 or > 65535 ||
            string.IsNullOrWhiteSpace(options.Name) ||
            string.IsNullOrWhiteSpace(options.Username) ||
            string.IsNullOrWhiteSpace(options.Password))
        {
            throw new InvalidOperationException(
                "Database configuration is incomplete. Set Database:Host, Database:Port, Database:Name, Database:Username, and Database:Password.");
        }
    }
}
