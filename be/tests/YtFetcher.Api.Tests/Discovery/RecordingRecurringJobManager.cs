using Hangfire;
using Hangfire.Common;

namespace YtFetcher.Api.Tests.Discovery;

/// <summary>
/// Captures Hangfire recurring-job registrations without touching real storage.
/// </summary>
public sealed class RecordingRecurringJobManager : IRecurringJobManager
{
    private readonly List<RecurringJobRegistration> registrations = [];

    public IReadOnlyList<RecurringJobRegistration> Registrations => registrations;

    public void AddOrUpdate(string recurringJobId, Job job, string cronExpression, RecurringJobOptions options)
    {
        registrations.Add(new RecurringJobRegistration(
            recurringJobId,
            job.Type.FullName ?? job.Type.Name,
            job.Method.Name,
            cronExpression,
            options.TimeZone));
    }

    public void Trigger(string recurringJobId)
    {
    }

    public void RemoveIfExists(string recurringJobId)
    {
    }
}

public sealed record RecurringJobRegistration(
    string RecurringJobId,
    string JobType,
    string MethodName,
    string CronExpression,
    TimeZoneInfo? TimeZone);
