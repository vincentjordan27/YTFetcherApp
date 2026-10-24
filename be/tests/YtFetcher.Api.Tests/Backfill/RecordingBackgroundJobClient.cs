using Hangfire;
using Hangfire.Common;
using Hangfire.States;

namespace YtFetcher.Api.Tests.Backfill;

/// <summary>
/// Records what would be written to Hangfire storage without starting a Hangfire server.
/// </summary>
public sealed class RecordingBackgroundJobClient : IBackgroundJobClient
{
    public List<Job> CreatedJobs { get; } = [];

    public string Create(Job job, IState state)
    {
        CreatedJobs.Add(job);
        return $"job-{CreatedJobs.Count}";
    }

    public bool ChangeState(string jobId, IState state, string expectedState) => false;
}
