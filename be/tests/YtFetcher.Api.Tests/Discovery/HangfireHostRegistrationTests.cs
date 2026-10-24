using System.Reflection;
using YtFetcher.Application.Quotas;
using Hangfire;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using YtFetcher.Infrastructure.Jobs;

namespace YtFetcher.Api.Tests.Discovery;

[Collection(ApiStartupCollection.Name)]
public class HangfireHostRegistrationTests
{
    [Fact]
    public void Api_host_registers_the_daily_processing_recurring_job_without_starting_a_server()
    {
        using var databaseConfiguration = new HostConfigurationScope();
        using var factory = new TestApiFactory();

        _ = factory.Services;

        // Exactly three recurring ids: discovery, the merged daily processing run, and run retention. The two
        // ids daily processing merged are asserted absent below.
        Assert.Equal(3, factory.RecurringJobs.Registrations.Count);
        Assert.Equal(
            ["backfill-clear-history", "channel-discovery", "daily-processing"],
            factory.RecurringJobs.Registrations.Select(entry => entry.RecurringJobId).Order().ToArray());

        var registration = Assert.Single(
            factory.RecurringJobs.Registrations,
            candidate => candidate.RecurringJobId == "channel-discovery");
        Assert.Equal("channel-discovery", registration.RecurringJobId);
        Assert.Equal("0 1 * * *", registration.CronExpression);
        Assert.Equal(PacificQuotaWindow.TimeZone, registration.TimeZone);
        Assert.Equal(typeof(ChannelDiscoveryHangfireJob).FullName, registration.JobType);
        Assert.Equal(nameof(ChannelDiscoveryHangfireJob.RunAsync), registration.MethodName);

        // One registration owns ledger setup, statistics polling, quota finalization/gate opening, and the
        // bounded dispatch pass, in the Pacific quota timezone at 00:01.
        var dailyProcessing = Assert.Single(
            factory.RecurringJobs.Registrations,
            candidate => candidate.RecurringJobId == "daily-processing");
        Assert.Equal("1 0 * * *", dailyProcessing.CronExpression);
        Assert.Equal(PacificQuotaWindow.TimeZone, dailyProcessing.TimeZone);
        Assert.Equal(typeof(DailyProcessingHangfireJob).FullName, dailyProcessing.JobType);
        Assert.Equal(nameof(DailyProcessingHangfireJob.RunAsync), dailyProcessing.MethodName);

        var retention = Assert.Single(
            factory.RecurringJobs.Registrations,
            candidate => candidate.RecurringJobId == "backfill-clear-history");
        Assert.Equal("0 3 * * *", retention.CronExpression);
        Assert.Equal(TimeZoneInfo.Utc, retention.TimeZone);
        Assert.Equal(typeof(BackfillClearHistoryHangfireJob).FullName, retention.JobType);
        Assert.Equal(nameof(BackfillClearHistoryHangfireJob.RunAsync), retention.MethodName);

        // The two merged ids are gone from registration: their work now belongs to daily processing.
        Assert.DoesNotContain(
            factory.RecurringJobs.Registrations,
            candidate => candidate.RecurringJobId == "backfill-daily-quota-window-phase");
        Assert.DoesNotContain(
            factory.RecurringJobs.Registrations,
            candidate => candidate.RecurringJobId == "backfill-dispatch-pump");

        // A whole daily run must never be silently replayed after partial success, because a provider request
        // may already have spent quota even when its persistence failed.
        var automaticRetry = typeof(DailyProcessingHangfireJob)
            .GetMethod(nameof(DailyProcessingHangfireJob.RunAsync))!
            .GetCustomAttribute<AutomaticRetryAttribute>();
        Assert.NotNull(automaticRetry);
        Assert.Equal(0, automaticRetry.Attempts);

        Assert.NotNull(factory.Services.GetRequiredService<IRecurringJobManager>());

        using (var scope = factory.Services.CreateScope())
        {
            Assert.NotNull(scope.ServiceProvider.GetRequiredService<ChannelDiscoveryHangfireJob>());
            Assert.NotNull(scope.ServiceProvider.GetRequiredService<DailyProcessingHangfireJob>());
            Assert.NotNull(scope.ServiceProvider.GetRequiredService<BackfillClearHistoryHangfireJob>());
        }

        Assert.DoesNotContain(
            factory.Services.GetServices<IHostedService>(),
            service => service.GetType().FullName?.StartsWith("Hangfire.", StringComparison.Ordinal) == true);
    }
}
