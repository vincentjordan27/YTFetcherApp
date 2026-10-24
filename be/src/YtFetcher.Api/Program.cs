using System.Text.Json.Serialization;
using YtFetcher.Application.Quotas;
using FluentValidation;
using Hangfire;
using Hangfire.Dashboard;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Cors.Infrastructure;
using Serilog;
using Serilog.Events;
using YtFetcher.Api.Authentication;
using YtFetcher.Api.Configuration;
using YtFetcher.Api.Endpoints.Authentication;
using YtFetcher.Api.Endpoints.BackfillRuns;
using YtFetcher.Api.Endpoints.Channels;
using YtFetcher.Api.Endpoints.DiscoveryKeywords;
using YtFetcher.Api.Endpoints.Health;
using YtFetcher.Api.Endpoints.Usage;
using YtFetcher.Api.Endpoints.Videos;
using YtFetcher.Api.Observability;
using YtFetcher.Application;
using YtFetcher.Application.Abstractions.Services;
using YtFetcher.Infrastructure;
using YtFetcher.Infrastructure.Configuration;
using YtFetcher.Infrastructure.Jobs;

var builder = WebApplication.CreateBuilder(args);

// The authentication mode is read once, before any service is registered, so a misconfigured Production host
// fails here with a readable error instead of starting anonymously.
var authenticationConfiguration = AuthenticationRuntimeConfiguration.Load(
    builder.Configuration,
    builder.Environment);

builder.AddApiObservability();

builder.Services.AddApplication(authenticationConfiguration.AuthenticationEnabled);
builder.Services.AddInfrastructure(builder.Configuration, authenticationConfiguration);
builder.Services.AddHttpContextAccessor();
builder.Services.AddScoped<ICurrentActor, CurrentActor>();
builder.Services.AddValidatorsFromAssemblyContaining<Program>();
builder.Services.AddHangfireServer();
builder.Services.ConfigureHttpJsonOptions(options =>
    options.SerializerOptions.Converters.Add(new JsonStringEnumConverter()));

// The frontend runs on its own origin and sends the session cookie, so CORS allows exactly the configured
// origins with credentials in every environment. An unconfigured origin list rejects every cross-origin
// request rather than falling back to a wildcard.
const string frontendCorsPolicy = "frontend";
builder.Services.AddCors();
builder.Services.AddOptions<CorsOptions>().Configure<IAuthenticationRuntimeConfiguration>(
    (corsOptions, runtimeConfiguration) => corsOptions.AddPolicy(
        frontendCorsPolicy,
        policy => policy
            .SetIsOriginAllowed(origin => runtimeConfiguration.AllowedFrontendOrigins.Contains(
                origin,
                StringComparer.OrdinalIgnoreCase))
            .AllowCredentials()
            .WithMethods("GET", "POST", "DELETE")
            .WithHeaders("Accept", "Content-Type")));

if (authenticationConfiguration.AuthenticationEnabled)
{
    builder.Services.AddAuthentication(SessionAuthentication.SchemeName)
        .AddScheme<AuthenticationSchemeOptions, SessionCookieAuthenticationHandler>(
            SessionAuthentication.SchemeName,
            configureOptions: null);

    // Enabled mode protects every endpoint by default, so a future endpoint group cannot become public by
    // omission. Health and the auth group opt out explicitly.
    builder.Services.AddAuthorization(options => options.FallbackPolicy =
        new AuthorizationPolicyBuilder().RequireAuthenticatedUser().Build());
}
else
{
    builder.Services.AddAuthorization();
}

// Swagger is a local development tool and never ships in Production.
if (builder.Environment.IsDevelopment())
{
    builder.Services.AddEndpointsApiExplorer();
    builder.Services.AddSwaggerGen();
}

var app = builder.Build();

app.UseSerilogRequestLogging(options =>
{
    options.GetLevel = (httpContext, _, exception) =>
    {
        if (exception is not null || httpContext.Response.StatusCode >= StatusCodes.Status500InternalServerError)
        {
            return LogEventLevel.Error;
        }

        if (httpContext.Request.Path.StartsWithSegments(HangfireDashboardMapping.Path))
        {
            return LogEventLevel.Debug;
        }

        return LogEventLevel.Information;
    };
});
app.UseMiddleware<UnhandledApiExceptionLoggingMiddleware>();

// CORS runs before authentication so a browser preflight is answered without a session.
app.UseCors(frontendCorsPolicy);

if (authenticationConfiguration.AuthenticationEnabled)
{
    // The dashboard authorization filter reads the established principal, so authentication must be earlier in
    // the pipeline than the dashboard mapping below.
    app.UseAuthentication();
    app.UseMiddleware<FrontendOriginGuardMiddleware>();
}

var hangfireDashboardEnabled = builder.Configuration.GetValue<bool>("Hangfire:Dashboard:Enabled");

// Mapping is an explicit operator choice. Authentication, when enabled, adds a session requirement below.
if (HangfireDashboardMapping.ShouldMap(hangfireDashboardEnabled))
{
    if (authenticationConfiguration.AuthenticationEnabled)
    {
        app.UseHangfireDashboard(
            HangfireDashboardMapping.Path,
            new DashboardOptions
            {
                Authorization =
                [new HangfireDashboardAuthorizationFilter(app.Services.GetRequiredService<IHttpContextAccessor>())]
            });
    }
    else
    {
        app.UseHangfireDashboard(HangfireDashboardMapping.Path);
    }
}

if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}

// Authorization is applied after the development-only middleware above so Swagger and the dashboard keep their
// own behavior, and only the mapped API endpoints are subject to the fallback policy.
if (authenticationConfiguration.AuthenticationEnabled)
{
    app.UseAuthorization();
}

app.MapHealthEndpoints();
app.MapAuthenticationEndpoints();
app.MapChannelEndpoints();
app.MapBackfillRunEndpoints();
app.MapDiscoveryKeywordEndpoints();
app.MapVideoEndpoints();
app.MapUsageEndpoints();

// The quota ledger starts at 00:01 Pacific. Discovery follows at 01:00 Pacific, after the daily job
// has initialized the new quota window.
var quotaTimeZone = PacificQuotaWindow.TimeZone;
var recurringJobs = app.Services.GetRequiredService<IRecurringJobManager>();

recurringJobs.AddOrUpdate<ChannelDiscoveryHangfireJob>(
    recurringJobId: "channel-discovery",
    methodCall: job => job.RunAsync(CancellationToken.None),
    cronExpression: Cron.Daily(1, 0),
    options: new RecurringJobOptions { TimeZone = quotaTimeZone });

// One ordered daily job owns ledger setup, tracked-video statistics, quota finalization and gate opening,
// and one bounded channel dispatch pass, all in the Pacific quota timezone.
recurringJobs.AddOrUpdate<DailyProcessingHangfireJob>(
    recurringJobId: "daily-processing",
    methodCall: job => job.RunAsync(CancellationToken.None),
    cronExpression: Cron.Daily(0, 1),
    options: new RecurringJobOptions { TimeZone = quotaTimeZone });

// The two replaced ids are dropped explicitly, so an environment that already registered them does not keep
// running the jobs this delivery merges into daily-processing.
recurringJobs.RemoveIfExists("backfill-daily-quota-window-phase");
recurringJobs.RemoveIfExists("daily-quota-window-phase");
recurringJobs.RemoveIfExists("backfill-dispatch-pump");

// Retention clears expired history on UTC at 03:00; the job itself disables automatic retry because the
// next daily run is the recovery mechanism for a bounded cleanup.
recurringJobs.AddOrUpdate<BackfillClearHistoryHangfireJob>(
    recurringJobId: "backfill-clear-history",
    methodCall: job => job.RunAsync(CancellationToken.None),
    cronExpression: Cron.Daily(3, 0),
    options: new RecurringJobOptions { TimeZone = TimeZoneInfo.Utc });

// The obsolete retention id is dropped explicitly so an environment that already registered it does not
// keep running a job this delivery replaces.
recurringJobs.RemoveIfExists("backfill-run-retention");

app.Run();
