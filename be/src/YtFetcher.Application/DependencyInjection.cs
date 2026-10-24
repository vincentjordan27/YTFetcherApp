using Microsoft.Extensions.DependencyInjection;
using YtFetcher.Application.Abstractions.UseCases;
using YtFetcher.Application.Authentication.UseCases;
using YtFetcher.Application.Backfill.UseCases;
using YtFetcher.Application.BackfillRuns.UseCases;
using YtFetcher.Application.Channels.UseCases;
using YtFetcher.Application.Discovery.UseCases;
using YtFetcher.Application.DiscoveryKeywords.UseCases;
using YtFetcher.Application.Usage.UseCases;
using YtFetcher.Application.Videos.UseCases;

namespace YtFetcher.Application;

public static class DependencyInjection
{
    /// <summary>
    /// Registers the feature use cases. The authentication mode is part of the composition decision, because
    /// the anonymous mode must not require a Firebase verifier or a session issuer at all.
    /// </summary>
    public static IServiceCollection AddApplication(
        this IServiceCollection services,
        bool authenticationEnabled)
    {
        services.AddScoped<IChannelUseCase, ChannelUseCase>();
        services.AddScoped<IRunChannelDiscoveryUseCase, RunChannelDiscoveryUseCase>();
        services.AddScoped<IBackfillUseCase, BackfillUseCase>();
        services.AddScoped<IBackfillSchedulingUseCase, BackfillSchedulingUseCase>();
        services.AddScoped<IRunDailyProcessingUseCase, DailyProcessingUseCase>();
        services.AddScoped<IVideoUseCase, VideoUseCase>();
        services.AddScoped<IUsageUseCase, UsageUseCase>();
        services.AddScoped<IDiscoveryKeywordUseCase, DiscoveryKeywordUseCase>();
        services.AddScoped<IBackfillRunUseCase, BackfillRunUseCase>();
        services.AddScoped<IRunBackfillClearHistoryUseCase, BackfillClearHistoryUseCase>();

        if (authenticationEnabled)
        {
            services.AddScoped<IAuthenticationUseCase, AuthenticationUseCase>();
        }
        else
        {
            services.AddScoped<IAuthenticationUseCase, DisabledAuthenticationUseCase>();
        }

        return services;
    }
}
