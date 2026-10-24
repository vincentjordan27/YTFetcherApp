using System.Globalization;
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
    private const int MinimumDailyQuotaLimit = 1;

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
        services.AddSingleton<IDiscoveryPageLimitPolicy, DiscoveryPageLimitPolicy>();
        services.AddScoped<IBackfillRepository, BackfillRepository>();
        services.AddScoped<IBackfillRunRepository, BackfillRunRepository>();
        services.AddScoped<IDailyQuotaRepository, DailyQuotaRepository>();
        services.AddScoped<INewUploadMonitoringRepository, NewUploadMonitoringRepository>();
        services.AddScoped<IVideoRepository, VideoRepository>();
        services.AddScoped<IVideoStatisticsRepository, VideoStatisticsRepository>();
        services.AddScoped<IBackfillActivityLogger, BackfillActivityLogger>();
        services.AddScoped<INewUploadMonitorActivityLogger, NewUploadMonitorActivityLogger>();
        services.AddScoped<IBackfillScheduler, HangfireBackfillScheduler>();
        services.AddSingleton<IBackfillPageLimitPolicy, BackfillPageLimitPolicy>();
        services.AddSingleton<IBackfillDispatchBatchSizePolicy, BackfillDispatchBatchSizePolicy>();
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

        // The backfill limits and batch size are startup requirements: a missing or invalid value
        // must stop the host with a readable error instead of failing later inside a job.
        var startupSettings = new AppSettingsService(configuration);
        ValidateYouTubeBackfillOptions(startupSettings);
        ValidateBackfillPageLimit(startupSettings);
        ValidateBackfillDispatchBatchSize(startupSettings);
        ValidateDiscoveryPageLimit(startupSettings);

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

    private static void ValidateYouTubeBackfillOptions(IAppSettingsService appSettingsService)
    {
        var queriesLimit = ReadRequiredInt(
            appSettingsService,
            QuotaConfigurationKeys.QueriesDailyQuotaLimit);
        var batchStatsLimit = ReadRequiredInt(
            appSettingsService,
            QuotaConfigurationKeys.VideoBatchGetStatsDailyQuotaLimit);
        var searchLimit = ReadRequiredInt(
            appSettingsService,
            QuotaConfigurationKeys.SearchQueriesDailyQuotaLimit);
        var maxApprovedChannels = ReadRequiredInt(
            appSettingsService,
            QuotaConfigurationKeys.MaxDailyApprovedChannels);

        if (queriesLimit < MinimumDailyQuotaLimit ||
            batchStatsLimit < MinimumDailyQuotaLimit ||
            searchLimit < MinimumDailyQuotaLimit)
        {
            throw new InvalidOperationException(
                "Every daily provider quota limit must be at least " + MinimumDailyQuotaLimit + ".");
        }

        if (maxApprovedChannels <= 0)
        {
            throw new InvalidOperationException(
                $"Configuration value '{QuotaConfigurationKeys.MaxDailyApprovedChannels}' must be greater than 0.");
        }

        if (maxApprovedChannels > queriesLimit || maxApprovedChannels > batchStatsLimit)
        {
            throw new InvalidOperationException(
                $"Configuration value '{QuotaConfigurationKeys.MaxDailyApprovedChannels}' cannot exceed " +
                $"'{QuotaConfigurationKeys.QueriesDailyQuotaLimit}' or " +
                $"'{QuotaConfigurationKeys.VideoBatchGetStatsDailyQuotaLimit}'.");
        }
    }

    /// <summary>
    /// Constructs the page-limit policy once while the host is built so a missing, non-numeric, or
    /// negative page limit stops startup with the policy's readable sentence.
    /// </summary>
    private static void ValidateBackfillPageLimit(IAppSettingsService appSettingsService) =>
        _ = new BackfillPageLimitPolicy(appSettingsService);

    /// <summary>
    /// Constructs the dispatch-bound policy once while the host is built, so a missing, non-numeric, or
    /// non-positive value stops startup with the policy's readable sentence.
    /// </summary>
    private static void ValidateBackfillDispatchBatchSize(IAppSettingsService appSettingsService) =>
        _ = new BackfillDispatchBatchSizePolicy(appSettingsService);

    private static void ValidateDiscoveryPageLimit(IAppSettingsService appSettingsService) =>
        _ = new DiscoveryPageLimitPolicy(appSettingsService);

    private static int ReadRequiredInt(IAppSettingsService appSettingsService, string key)
    {
        var value = appSettingsService.GetValue(key);

        return int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsed)
            ? parsed
            : throw new InvalidOperationException(
                $"Configuration value '{key}' is missing or not a whole number.");
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
