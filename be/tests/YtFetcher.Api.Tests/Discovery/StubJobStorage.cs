using Hangfire;
using Hangfire.Common;
using Hangfire.Storage;

namespace YtFetcher.Api.Tests.Discovery;

/// <summary>
/// Minimal Hangfire storage for the test host. When the Development dashboard middleware is mapped,
/// Hangfire resolves the configured storage, and the production PostgreSQL storage opens a database
/// connection in its constructor. Tests must run without a live database, so this stub is registered
/// instead. No test renders the dashboard, so these members are never exercised.
/// </summary>
internal sealed class StubJobStorage : JobStorage
{
    private const string Reason =
        "The test host does not run Hangfire background work or render the Hangfire dashboard.";

    public override IMonitoringApi GetMonitoringApi() => throw new NotSupportedException(Reason);

    public override IStorageConnection GetConnection() => throw new NotSupportedException(Reason);
}
